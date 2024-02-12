using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RavenDbTest
{
    internal class Product
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public bool IsSynchronized { get; set; }
    }
}
