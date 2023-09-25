using FileIO;
using System;
using System.Diagnostics;
using System.Reflection;
using Utils;



namespace Versioning {

  public class Program {

    static int Main(string[] args) {
      try {

        string myVers = Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

        var adapter = ConsoleAdapter.GetInstance();
        adapter.RegisterInvokationTarget(new KVersioningHelper());

        if(args.Length == 0) {
          try {
            Console.SetWindowSize(120, 50);
            Console.SetBufferSize(120, 8000);
          }
          catch {
          }

          Console.WriteLine($"WELCOME to the 'kVersioningUtil' v{myVers}");
          Console.WriteLine("were running in console mode (hit F1 to see possible commands or type 'exit' to leave):");

          adapter.EnterConsoleLoop();
          return 0;
        }
        else {
          Console.WriteLine($"'kVersioningUtil' v{myVers} running in commandline mode...");
          adapter.InvokeCommand(args);
        }

      }
      catch (Exception ex) {
        var old = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("ERROR: " + ex.Message);
        Console.WriteLine(ex.StackTrace);
        Console.ForegroundColor = old;
        return 1;
      }
      Console.WriteLine("completed...");
      return 0;
    }

  }

}
