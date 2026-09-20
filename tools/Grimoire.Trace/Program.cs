using Grimoire.Trace;

// Two verbs, one pair of inputs: the requirement ids and proof kinds registered in
// docs/capabilities/, and the level and req traits carried by the built test assemblies.
//
//   check  the gate of Constitution IV.3 — four rules, writes nothing
//   write  the single documented command of Constitution IV.4 — produces docs/trace.md
try
{
    var verb = args.Length > 0 ? args[0] : null;
    var configuration = ConfigurationOption(args);

    if (verb is not ("check" or "write"))
    {
        Console.Error.WriteLine("usage: Grimoire.Trace <check|write> [--configuration <Debug|Release>]");
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

    var violations = TraceCheck.Run(requirements, tests);
    if (violations.Count > 0)
    {
        Console.Error.WriteLine($"trace-check: {violations.Count} violation(s)");
        foreach (var violation in violations)
        {
            Console.Error.WriteLine($"  {violation}");
        }

        return 1;
    }

    Console.WriteLine($"trace-check: {requirements.Count} requirements, {tests.Count} tests, no violations");
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
