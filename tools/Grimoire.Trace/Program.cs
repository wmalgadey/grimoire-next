using Grimoire.Trace;

// Two verbs, one pair of inputs: the requirement ids and proof kinds registered in
// docs/capabilities/, and the level and req traits carried by the built test assemblies.
//
//   check             the gate of Constitution IV.3 — writes nothing. The three conditions that
//                     hold at any moment; CI calls this on every push
//   check --complete  those three and the fourth, a `test` requirement with no test. IV.3 applies
//                     it where a feature lands on main; CI calls this on a PR whose base is main
//   write             the single documented command of Constitution IV.4 — produces docs/trace.md
try
{
    var verb = args.Length > 0 ? args[0] : null;
    var configuration = ConfigurationOption(args);
    var complete = args.Contains("--complete", StringComparer.Ordinal);

    if (verb is not ("check" or "write"))
    {
        Console.Error.WriteLine("usage: Grimoire.Trace <check [--complete]|write> [--configuration <Debug|Release>]");
        return 2;
    }

    if (Unrecognised(args) is { } unrecognised)
    {
        Console.Error.WriteLine($"trace-check: unrecognised argument \"{unrecognised}\"");
        Console.Error.WriteLine("usage: Grimoire.Trace <check [--complete]|write> [--configuration <Debug|Release>]");
        return 2;
    }

    var layout = RepositoryLayout.Discover();
    var requirements = CapabilityRegistry.Read(layout.CapabilitiesDirectory);
    var tests = TestCatalogue.Read(layout.TestAssemblies(configuration));

    if (verb == "write")
    {
        File.WriteAllText(layout.TraceDocument, TraceDocument.Render(requirements, tests));
        Console.WriteLine($"wrote {Path.GetRelativePath(layout.Root, layout.TraceDocument)}: {requirements.Count} requirements, {tests.Count} tests");
        return 0;
    }

    var violations = TraceCheck.Run(requirements, tests, complete);
    if (violations.Count > 0)
    {
        Console.Error.WriteLine($"trace-check: {violations.Count} violation(s)");
        foreach (var violation in violations)
        {
            Console.Error.WriteLine($"  {violation}");
        }

        return 1;
    }

    Console.WriteLine(
        $"trace-check{(complete ? " --complete" : string.Empty)}: {requirements.Count} requirements, {tests.Count} tests, no violations");
    return 0;
}
catch (TraceInputException failure)
{
    Console.Error.WriteLine($"trace-check: {failure.Message}");
    return 2;
}

static string? ConfigurationOption(string[] arguments)
{
    var at = Array.IndexOf(arguments, "--configuration");
    return at >= 0 && at + 1 < arguments.Length ? arguments[at + 1] : null;
}

/// <summary>
/// The first argument the tool does not understand, or null. A mistyped <c>--complete</c> would
/// otherwise run the weaker check and report success, which is the one failure a gate must not
/// have (Constitution IV.3: what the check cannot read, it fails on rather than skips).
/// </summary>
static string? Unrecognised(string[] arguments)
{
    var configurationValue = Array.IndexOf(arguments, "--configuration") + 1;

    for (var at = 1; at < arguments.Length; at++)
    {
        if (arguments[at] is "--complete" or "--configuration" || at == configurationValue)
        {
            continue;
        }

        return arguments[at];
    }

    return null;
}
