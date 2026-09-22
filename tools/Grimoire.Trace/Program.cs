using Grimoire.Trace;

// Three verbs, one pair of inputs: the requirement ids and proof kinds registered in
// docs/capabilities/, and the level and req traits carried by the built test assemblies.
//
//   check             the gate of Constitution IV.3 — writes nothing. The three conditions that
//                     hold at any moment; CI calls this on every push
//   check --complete  those three and the fourth, a `test` requirement with no test. IV.3 applies
//                     it where a feature lands on main; CI calls this on a PR whose base is main
//   write             the single documented command of Constitution IV.4 — produces docs/trace.md
//   summary           the counts behind the requirements badge, as JSON on stdout. Writes nothing
//                     and judges nothing: no count makes it fail, so it is a measurement and not a
//                     second gate (Constitution II.2)
const string Usage = "usage: Grimoire.Trace <check [--complete]|write|summary> [--configuration <Debug|Release>]";

try
{
    var verb = args.Length > 0 ? args[0] : null;

    if (verb is not ("check" or "write" or "summary"))
    {
        Console.Error.WriteLine(Usage);
        return 2;
    }

    // A failure of the tool itself is prefixed with the tool's name, not with `trace-check`: two of
    // the three verbs are not the gate, and only the gate's verdict below says `trace-check`.
    //
    // Every argument is read against the verb that was given, and anything that does not fit it
    // ends the run. A gate that quietly runs a different command shape than the caller asked for
    // is the one failure a gate must not have (Constitution IV.3: what the check cannot read, it
    // fails on rather than skips) — a mistyped `--complet`, a `--complete` handed to `write`, and
    // a `--configuration` with nothing after it are all that failure.
    string? configuration = null;
    var complete = false;

    for (var at = 1; at < args.Length; at++)
    {
        switch (args[at])
        {
            case "--complete" when verb == "check":
                complete = true;
                break;

            case "--complete":
                Console.Error.WriteLine($"Grimoire.Trace: --complete belongs to `check`; `{verb}` has no such call");
                Console.Error.WriteLine(Usage);
                return 2;

            case "--configuration" when at + 1 < args.Length && !args[at + 1].StartsWith("--", StringComparison.Ordinal):
                configuration = args[++at];
                break;

            case "--configuration":
                Console.Error.WriteLine("Grimoire.Trace: --configuration needs a value, Debug or Release");
                Console.Error.WriteLine(Usage);
                return 2;

            default:
                Console.Error.WriteLine($"Grimoire.Trace: unrecognised argument \"{args[at]}\"");
                Console.Error.WriteLine(Usage);
                return 2;
        }
    }

    var layout = RepositoryLayout.Discover();
    var requirements = CapabilityRegistry.Read(layout.CapabilitiesDirectory);
    var tests = TestCatalogue.Read(layout.TestAssemblies(configuration));

    // Nothing but the two readers stands behind the count, so the badge and the gate can never
    // disagree about what is registered.
    if (verb == "summary")
    {
        Console.WriteLine(TraceSummary.Render(requirements, tests));
        return 0;
    }

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
    Console.Error.WriteLine($"Grimoire.Trace: {failure.Message}");
    return 2;
}
