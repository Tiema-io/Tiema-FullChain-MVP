//Tiema.Runtime/Program.cs 
using System.Text.Json;
using Tiema.Runtime;
using Tiema.Runtime.Models;
using Tiema.Runtime.Services;

class Program
    {
    static void Main(string[] args)
    {
        Console.WriteLine("=== Tiema Runtime v0.1 ====");

        var config=Utility. LoadConfiguration("tiema.config.json");

        var container=new TiemaContainer(config,new BuiltInTagService(),new BuiltInMessageService());

        container.LoadPlugins();

        container.Run();
    }

  


}