using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Oriel;

namespace BackOffice;

/// <summary>What a customer pays for.</summary>
public enum Tier
{
    Free,
    Pro,
    Enterprise,
}

/// <summary>A customer of the back office. <see cref="Id"/> 0 is one not saved yet.</summary>
public sealed record Customer(long Id, string Name, string Email, Tier Tier, decimal Balance, DateTimeOffset Joined);

/// <summary>Some rows of the customers that match, and how many match in all.</summary>
public sealed record CustomerPage(int Total, Customer[] Rows);

/// <summary>The columns customers sort by.</summary>
public enum SortBy
{
    Id,
    Name,
    Email,
    Tier,
    Balance,
    Joined,
}

/// <summary>A customer that cannot be saved as it is: what is wrong, by field.</summary>
[ServiceError("invalid")]
public sealed class InvalidCustomerException(Dictionary<string, string> errors) : Exception("The customer has fields to fix.")
{
    /// <summary>What is wrong with each field, by its name in the page: <c>email</c>.</summary>
    public Dictionary<string, string> Errors { get; } = errors;
}

/// <summary>A customer that is gone, deleted by another window.</summary>
[ServiceError("not-found")]
public sealed class CustomerNotFoundException(long id) : Exception($"Customer {id} is gone.")
{
    public long Id { get; } = id;
}

/// <summary>The customers: pages of them for the grid, and saving one from the form.</summary>
[Service(Concurrency = ServiceConcurrency.Serial)]
public sealed partial class Customers(SqliteConnection connection, int seed = 0)
{
    private bool _seeded;

    /// <summary>Fires with a customer saved, so that every window shows it.</summary>
    public Event<Customer> Saved { get; } = new();

    /// <summary>Fires with the id of a customer deleted.</summary>
    public Event<long> Deleted { get; } = new();

    /// <summary>
    /// The customers from <paramref name="offset"/>, <paramref name="limit"/> of
    /// them at most, whose name or email has <paramref name="search"/>.
    /// </summary>
    public CustomerPage Page(int offset, int limit, string? search = null, SortBy sort = SortBy.Id, bool descending = false)
    {
        if (!_seeded)
        {
            Seed(seed);
            _seeded = true;
        }
        limit = Math.Clamp(limit, 1, 1000);
        var where = string.IsNullOrWhiteSpace(search) ? "" : "where name like @search or email like @search";
        var column = sort.ToString().ToLowerInvariant();
        var order = $"{column} {(descending ? "desc" : "asc")}, id";

        using var count = Command($"select count(*) from customers {where}", search);
        var total = Convert.ToInt32(count.ExecuteScalar(), CultureInfo.InvariantCulture);

        using var select = Command($"select id, name, email, tier, balance, joined from customers {where} order by {order} limit @limit offset @offset", search);
        select.Parameters.AddWithValue("@limit", limit);
        select.Parameters.AddWithValue("@offset", Math.Max(0, offset));
        var rows = new List<Customer>(limit);
        using var reader = select.ExecuteReader();
        while (reader.Read())
            rows.Add(Read(reader));
        return new CustomerPage(total, [.. rows]);
    }

    /// <summary>Saves <paramref name="customer"/>: a new one when its id is 0. Returns it as saved.</summary>
    /// <exception cref="InvalidCustomerException">A field is not valid.</exception>
    /// <exception cref="CustomerNotFoundException">The customer is gone.</exception>
    public Customer Save(Customer customer)
    {
        var errors = new Dictionary<string, string>();
        var name = customer.Name.Trim();
        var email = customer.Email.Trim();
        if (name.Length == 0)
            errors["name"] = "A name is needed.";
        else if (name.Length > 80)
            errors["name"] = "80 characters at most.";
        if (!EmailPattern().IsMatch(email))
            errors["email"] = "Not an email address.";
        if (customer.Balance is < -1_000_000 or > 1_000_000)
            errors["balance"] = "Between -1,000,000 and 1,000,000.";
        if (errors.Count > 0)
            throw new InvalidCustomerException(errors);

        SqliteCommand command;
        if (customer.Id == 0)
        {
            command = connection.CreateCommand();
            command.CommandText = "insert into customers (name, email, tier, balance, joined) values (@name, @email, @tier, @balance, @joined) returning id";
            command.Parameters.AddWithValue("@joined", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        }
        else
        {
            command = connection.CreateCommand();
            command.CommandText = "update customers set name = @name, email = @email, tier = @tier, balance = @balance where id = @id returning id";
            command.Parameters.AddWithValue("@id", customer.Id);
        }
        using (command)
        {
            command.Parameters.AddWithValue("@name", name);
            command.Parameters.AddWithValue("@email", email);
            command.Parameters.AddWithValue("@tier", (int)customer.Tier);
            command.Parameters.AddWithValue("@balance", (double)customer.Balance);
            var id = command.ExecuteScalar() is long saved ? saved : throw new CustomerNotFoundException(customer.Id);
            var stored = Get(id);
            Saved.Emit(stored);
            return stored;
        }
    }

    /// <summary>Deletes the customer <paramref name="id"/>.</summary>
    /// <exception cref="CustomerNotFoundException">It is gone already.</exception>
    public void Delete(long id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "delete from customers where id = @id";
        command.Parameters.AddWithValue("@id", id);
        if (command.ExecuteNonQuery() == 0)
            throw new CustomerNotFoundException(id);
        Deleted.Emit(id);
    }

    /// <summary>Fills an empty database with <paramref name="count"/> customers made up, the same on every machine.</summary>
    [ServiceIgnore]
    public void Seed(int count)
    {
        using (var create = connection.CreateCommand())
        {
            create.CommandText = """
                create table if not exists customers (
                  id integer primary key, name text not null, email text not null,
                  tier integer not null, balance real not null, joined text not null);
                create index if not exists customers_name on customers (name);
                create index if not exists customers_balance on customers (balance);
                """;
            create.ExecuteNonQuery();
        }
        using (var existing = connection.CreateCommand())
        {
            existing.CommandText = "select count(*) from customers";
            if (Convert.ToInt64(existing.ExecuteScalar(), CultureInfo.InvariantCulture) > 0)
                return;
        }

        string[] first = ["Ada", "Grace", "Alan", "Linus", "Margaret", "Dennis", "Barbara", "Ken", "Frances", "Edsger", "Radia", "Niklaus", "Hedy", "Tim", "Katherine", "Guido"];
        string[] last = ["Lovelace", "Hopper", "Turing", "Torvalds", "Hamilton", "Ritchie", "Liskov", "Thompson", "Allen", "Dijkstra", "Perlman", "Wirth", "Lamarr", "Berners-Lee", "Johnson", "van Rossum"];
        var random = new Random(42);
        var start = new DateTimeOffset(2015, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var transaction = connection.BeginTransaction();
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = "insert into customers (name, email, tier, balance, joined) values (@name, @email, @tier, @balance, @joined)";
        var name = insert.Parameters.Add("@name", SqliteType.Text);
        var email = insert.Parameters.Add("@email", SqliteType.Text);
        var tier = insert.Parameters.Add("@tier", SqliteType.Integer);
        var balance = insert.Parameters.Add("@balance", SqliteType.Real);
        var joined = insert.Parameters.Add("@joined", SqliteType.Text);
        for (var i = 1; i <= count; i++)
        {
            var (f, l) = (first[random.Next(first.Length)], last[random.Next(last.Length)]);
            name.Value = $"{f} {l}";
            email.Value = $"{f.ToLowerInvariant()}.{l.ToLowerInvariant().Replace(" ", "")}{i}@example.com";
            tier.Value = random.Next(10) switch { < 6 => 0, < 9 => 1, _ => 2 };
            balance.Value = Math.Round(random.NextDouble() * 20_000 - 5_000, 2);
            joined.Value = start.AddMinutes(random.Next(5_000_000)).ToString("O", CultureInfo.InvariantCulture);
            insert.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    private Customer Get(long id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "select id, name, email, tier, balance, joined from customers where id = @id";
        command.Parameters.AddWithValue("@id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : throw new CustomerNotFoundException(id);
    }

    private SqliteCommand Command(string sql, string? search)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        if (!string.IsNullOrWhiteSpace(search))
            command.Parameters.AddWithValue("@search", "%" + search.Trim() + "%");
        return command;
    }

    private static Customer Read(SqliteDataReader r) => new(
        r.GetInt64(0),
        r.GetString(1),
        r.GetString(2),
        (Tier)r.GetInt32(3),
        (decimal)r.GetDouble(4),
        DateTimeOffset.Parse(r.GetString(5), CultureInfo.InvariantCulture));

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();
}
