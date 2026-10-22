using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using CsCheck;
using Oriel.Generated;
using Oriel.Ipc;

namespace Oriel.Tests.Ipc;

/// <summary>
/// <see cref="PageConnection.ReceiveAsync"/> with messages no page of the app
/// sends: whatever a page posts, nothing escapes, and only calls get replies.
/// </summary>
public class PageConnectionFuzzTests
{
    private sealed class Calculator() : ServiceDispatcher("Calc")
    {
        public override ValueTask<ValueWriter?> InvokeAsync(string method, JsonElement args, CallContext context)
        {
            switch (method)
            {
                case "Add":
                    Arguments.Count(args, "Calc.Add", 2, 2);
                    var sum = Arguments.Read(args, 0, "a", Json.ReadInt32) + Arguments.Read(args, 1, "b", Json.ReadInt32);
                    return new(w => Json.Write(w, sum));
                case "Echo":
                    Arguments.Count(args, "Calc.Echo", 1, 1);
                    var text = Arguments.Read(args, 0, "text", Json.ReadString);
                    return new(w => Json.Write(w, text));
                case "Fail":
                    throw new InvalidOperationException("no luck");
                default:
                    throw new MethodNotFoundException(Name, method);
            }
        }
    }

    /// <summary>
    /// A message, and whether it is a call, which must get exactly one reply,
    /// or something else, which must get none. Null when it is not known.
    /// </summary>
    private sealed record Message(string Text, bool? IsCall, long Id = 0, string Token = "");

    private static readonly Gen<string> s_token = Gen.OneOfConst("page1", "page2", "", "\\u0000", "\\ud800", "é");

    private static readonly Gen<string> s_method = Gen.OneOfConst(
        "Calc.Add", "Calc.Echo", "Calc.Fail", "Calc.Missing", "Calc", ".Add", "Calc.", "Nope.Add", "", "Calc.Add.More", "\\ud800.Add", "Calc.\\udc00");

    private static readonly Gen<string> s_args = Gen.OneOfConst(
        "[1,2]", "[2147483647,1]", "[1]", "[]", "[1,2,3]", "{}", "null", "\"x\"", "[\"a\",\"b\"]", "[1e400,1]",
        "[\"text\"]", "[\"\\ud800\"]", "[{\"\\ud800\":1}]", "[[[[]]]]", "[null,null]");

    /// <summary>Messages with the fields of a call, each of them right or wrong.</summary>
    private static readonly Gen<Message> s_structured =
        from t in Gen.OneOfConst("\"call\"", "\"hello\"", "\"result\"", "\"event\"", "\"CALL\"", "1", "null", null)
        from id in Gen.OneOfConst("1", "0", "-5", "9223372036854775807", "9223372036854775808", "1.5", "\"1\"", "null", null)
        from token in Gen.OneOf(s_token.Select(k => "\"" + k + "\""), Gen.OneOfConst<string?>("7", "null", null))
        from method in Gen.OneOf(s_method.Select(m => "\"" + m + "\""), Gen.OneOfConst<string?>("3", "[]", null))
        from args in Gen.OneOf(s_args, Gen.Const<string?>((string?)null))
        select Structured(t, id, token, method, args);

    /// <summary>
    /// Calls, to any method, with any arguments, their fields in any order,
    /// and maybe a property of a later protocol.
    /// </summary>
    private static readonly Gen<Message> s_call =
        from id in Gen.Long[0, long.MaxValue]
        from token in Gen.OneOfConst("page1", "page2")
        from method in s_method
        from args in s_args
        from extra in Gen.OneOfConst<string?>(null, null, "x", "\\ud800")
        from order in Gen.Int
        select Structured("\"call\"", id.ToString(System.Globalization.CultureInfo.InvariantCulture), $"\"{token}\"", $"\"{method}\"", args, extra, order);

    /// <summary>Calls with their text changed at random: cut, characters inserted, replaced or dropped.</summary>
    private static readonly Gen<Message> s_mutated =
        from call in s_call
        from edits in Gen.Select(Gen.Int[0, 3], Gen.Double[0, 1], Gen.OneOfConst("", "{", "}", "[", "\"", "\\", ",", ":", "\\u", "\uD800", "\0", "\u2028", "0", "null"))
            .Array[1, 4]
        select new Message(Mutate(call.Text, edits), IsCall: null);

    /// <summary>Text that is hardly JSON at all.</summary>
    private static readonly Gen<Message> s_noise = Gen.OneOf(
            Gen.String[0, 40],
            Gen.Char.Array[0, 40].Select(c => new string(c)),
            Gen.Int[1, 200].Select(n => new string('[', n) + new string(']', n)),
            Gen.Int[1, 200].Select(n => string.Concat(Enumerable.Repeat("{\"a\":", n)) + "1" + new string('}', n)),
            Gen.OneOfConst("", "null", "[]", "{}", "\"call\"", "{\"t\":\"call\"}", "\uFEFF{}", "{\"t\":\"call\",\"t\":\"hello\"}"))
        .Select(text => new Message(text, IsCall: false));

    [Fact]
    public void RepliesOnceToEachCallAndToNothingElse()
    {
        Gen.Frequency((2, s_call), (2, s_structured), (2, s_mutated), (1, s_noise)).Array[1, 12].Sample(messages =>
        {
            var posted = new ConcurrentQueue<string>();
            var services = new ServiceTable();
            services.Add(new Calculator());
            var page = new PageConnection(services, posted.Enqueue, limits: new PageLimits(MaxMessageLength: 4096, MaxMessageDepth: 64, MaxCallsInFlight: 100),
                showExceptionMessages: false);

            foreach (var message in messages)
            {
                var before = posted.Count;
                page.ReceiveAsync(message.Text).GetAwaiter().GetResult();
                var replies = posted.Skip(before).ToList();
                switch (message.IsCall)
                {
                    case true:
                        AssertReply(Assert.Single(replies), message.Id, message.Token);
                        break;
                    case false:
                        Assert.Empty(replies);
                        break;
                    case null:
                        // A changed call may still be one: then it gets its reply, well formed.
                        Assert.True(replies.Count <= 1, string.Join("\n", replies));
                        foreach (var reply in replies)
                            AssertReply(reply, null, null);
                        break;
                }
            }
        }, iter: 3000, threads: 1, print: messages => messages is null ? "(no sample)" : string.Join("\n", messages.Select(m => m.Text)));
    }

    /// <summary>Checks <paramref name="reply"/> is a result or an error, of the call it answers when it is known.</summary>
    private static void AssertReply(string reply, long? id, string? token)
    {
        using var doc = JsonDocument.Parse(reply);
        var root = doc.RootElement;
        Assert.Contains(root.GetProperty("t").GetString(), new[] { "result", "error" });
        var replyId = root.GetProperty("id").GetInt64();
        var replyToken = root.GetProperty("k").GetString();
        if (id is not null)
        {
            Assert.Equal(id, replyId);
            Assert.Equal(token, replyToken);
        }
        if (root.GetProperty("t").GetString() == "error")
            Assert.NotEqual(JsonValueKind.Undefined, root.GetProperty("e").ValueKind);
    }

    private static Message Structured(string? t, string? id, string? token, string? method, string? args, string? extra = null, int? order = null)
    {
        var fields = new List<string>();
        void Add(string name, string? value)
        {
            if (value is not null)
                fields.Add($"\"{name}\":{value}");
        }
        Add("t", t);
        Add("id", id);
        Add("k", token);
        Add("m", method);
        Add("a", args);
        if (extra is not null)
            fields.Add($"\"{extra}\":0");
        if (order is int seed)
        {
            var shuffled = fields.ToArray();
            new Random(seed).Shuffle(shuffled);
            fields = [.. shuffled];
        }
        var text = "{" + string.Join(",", fields) + "}";

        // What makes a call, as docs/protocol.md says: the type, an integer
        // id, a token and a method that are strings, and arguments. A token
        // with a lone surrogate is no string .NET reads, and a name with one
        // no name.
        var isCall = extra != "\\ud800" && t == "\"call\"" && id is not null && long.TryParse(id, out _) && token is not null && token.StartsWith('"')
            && !token.Contains("\\ud800", StringComparison.Ordinal) && method is not null && method.StartsWith('"')
            && !method.Contains("\\ud800", StringComparison.Ordinal) && !method.Contains("\\udc00", StringComparison.Ordinal) && args is not null;
        return isCall
            ? new Message(text, true, long.Parse(id!), JsonDocument.Parse(token!).RootElement.GetString()!)
            : new Message(text, false);
    }

    private static string Mutate(string text, (int Kind, double Where, string What)[] edits)
    {
        var s = new StringBuilder(text);
        foreach (var (kind, where, what) in edits)
        {
            var at = (int)(where * s.Length);
            switch (kind)
            {
                case 0:
                    s.Length = at; // cut
                    break;
                case 1:
                    s.Insert(at, what);
                    break;
                case 2 when at < s.Length:
                    s.Remove(at, 1).Insert(at, what);
                    break;
                case 3 when at < s.Length:
                    s.Remove(at, 1);
                    break;
            }
        }
        return s.ToString();
    }
}
