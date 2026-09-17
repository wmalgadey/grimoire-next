// Egress proxy composition root. Filled in by T103 (YARP, model route) and T104 (fetch route).
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.Run();

// The assembly is anchored for tests by Grimoire.Egress.EgressMarker, not by a Program type:
// two top-level-statement apps in one test process would collide on the global `Program` name.
