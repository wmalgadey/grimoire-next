// The composition root. The HTTP surface, the MCP endpoint and the static page are added by the
// tasks that own them; this file only starts the host.
var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.Run();
