using Oriel.Cli;

return args switch
{
    ["doctor"] => Doctor.Run(Console.Out, new SystemProbe()),
    ["dev", .. var rest] when rest.Length <= 1 => await Dev.RunAsync(rest.Length == 1 ? rest[0] : Environment.CurrentDirectory),
    ["--version" or "-v"] => Version(),
    ["help" or "--help" or "-h"] or [] => Help(0),
    _ => Help(1),
};

static int Version()
{
    Console.WriteLine(typeof(Doctor).Assembly.GetName().Version?.ToString(3));
    return 0;
}

static int Help(int code)
{
    (code == 0 ? Console.Out : Console.Error).WriteLine("""
        oriel: the command of Oriel apps.

          oriel doctor          checks what building, running and shipping an app needs here
          oriel dev [folder]    runs the app of the folder (default: this one) with its
                                frontend's dev server: editing the page reloads it, editing
                                C# restarts the app and rewrites the TypeScript client
          oriel --version

        https://github.com/Sutsikii/Oriel
        """);
    return code;
}
