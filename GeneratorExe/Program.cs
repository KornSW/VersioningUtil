using System;
using System.Diagnostics;
using Utils;

namespace Versioning {

  public class Program {

    static int Main(string[] args) {
      try {

        Console.SetWindowSize(120, 50);
        Console.SetBufferSize(120, 8000);

        var adapter = ConsoleAdapter.GetInstance();
        adapter.RegisterInvokationTarget(new KVersioningHelper());

        if(args.Length == 0) {
          Console.WriteLine("WELCOME - were running in console mode (hit F1 to see possible comands or type 'exit' to leave):");
          adapter.EnterConsoleLoop();
          return 0;
        }
        else {
          adapter.InvokeCommand(args);
        }

      }
      catch (Exception ex) {
        Console.WriteLine("ERROR: " + ex.Message);
        return 1;
      }
      Console.WriteLine("completed...");
      return 0;
    }

  }

}
