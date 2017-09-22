using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Sharpire
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length < 3)
            {
                return;
            }
            String server = args[0];
            String stagingKey = args[1];
            String language = args[2];

            (new EmpireStager(server, stagingKey, language)).execute();
        }
    }
}
