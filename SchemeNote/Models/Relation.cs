using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SchemeNote.Models
{
    public class Relation
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid FromNodeId { get; set; }
        public Guid ToNodeId { get; set; }

        public Guid RelationTypeId { get; set; }
        public RelationType RelationType { get; set; }

        public Guid SubjectId { get; set; }
    }
}
