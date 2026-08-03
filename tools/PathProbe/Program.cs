using System.Diagnostics;
using System.Reflection;

Console.WriteLine($"AppContext.BaseDirectory                       = {AppContext.BaseDirectory}");
Console.WriteLine($"Environment.ProcessPath                        = {Environment.ProcessPath}");
Console.WriteLine($"Path.GetDirectoryName(Environment.ProcessPath) = {Path.GetDirectoryName(Environment.ProcessPath)}");
Console.WriteLine($"Environment.CurrentDirectory                   = {Environment.CurrentDirectory}");
Console.WriteLine($"Assembly.GetExecutingAssembly().Location       = '{Assembly.GetExecutingAssembly().Location}'");
Console.WriteLine($"Process.GetCurrentProcess().MainModule.FileName= {Process.GetCurrentProcess().MainModule?.FileName}");
