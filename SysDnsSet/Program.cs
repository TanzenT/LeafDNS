using System.Linq;

namespace SysDnsSet
{
    static class Program
    {
        private static void Main(string[] args)
        {
            if (args.Contains("reset"))
                LeafDNS.Tools.SysDnsSet.ResetDns();
            else if (args.Contains("set"))
                LeafDNS.Tools.SysDnsSet.SetDns(args[1], args[2]);
        }
    }
}
