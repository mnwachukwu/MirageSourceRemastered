// Client entry point. The Velopack update hooks run FIRST, before any window exists, because an
// install or update step may need to exit the process outright.
using Mirage.Client.Shell;
using Velopack;

VelopackApp.Build().Run();

using var game = new MirageGame();
game.Run();
