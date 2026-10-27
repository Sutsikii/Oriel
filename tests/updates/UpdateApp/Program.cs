using Oriel.Updates;

// As an app does: first, so that Velopack's setup and updates can run their hooks.
Updates.Run();

// Run by tests/updates/run.ps1, which reads the report; when the setup starts
// the app after installing it, there is nothing to do.
var report = Environment.GetEnvironmentVariable("ORIEL_UPDATE_REPORT");
var source = Environment.GetEnvironmentVariable("ORIEL_UPDATE_SOURCE");
if (report is null || source is null)
    return 0;

void Report(string line) => File.AppendAllText(report, line + Environment.NewLine);

var updates = new Updates(source);
var status = updates.Status();
Report($"running {status.Version} installed={status.Installed}");
var found = await updates.Check();
Report($"found {found?.Version ?? "none"}");
if (found is null)
    return 0;
await updates.Download();
Report("downloaded");
updates.Restart(); // closes this process, installs 2.0.0, starts it: it reports again
return 0;

