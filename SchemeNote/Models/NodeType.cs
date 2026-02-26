using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SchemeNote.Models
{
    public class NodeType
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public string Name { get; set; } = "";

        public string ColorCode { get; set; } = "#FFFFFF";
    }
}
