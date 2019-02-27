using System.Linq;

namespace StartUpSet
{
    static class Program
    {
        private static void Main(string[] args)
        {
            if (args.Contains("unset"))
                LeafDNS.Tools.StartUpSet.SetRunWithStart(false, args[1], args[2]);
            else if (args.Contains("set"))
                LeafDNS.Tools.StartUpSet.SetRunWithStart(true, args[1], args[2]);
            else if (args.Contains("get"))
                LeafDNS.Tools.StartUpSet.GetRunWithStart(args[1]);
        }
    }
}
