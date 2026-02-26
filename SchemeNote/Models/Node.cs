using System;
using System.Collections.Generic;
using System.Collections.ObjectModel; // 추가됨
using System.ComponentModel.DataAnnotations;

namespace SchemeNote.Models
{
    public class Node
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public string Title { get; set; } = "";
        public string? Content { get; set; }

        public Guid SubjectId { get; set; }
        public Subject Subject { get; set; }

        public Guid? ParentId { get; set; }
        public Node Parent { get; set; }

        public Guid? NodeTypeId { get; set; }
        public NodeType? NodeType { get; set; }
        public virtual ObservableCollection<Node> Children { get; set; } = new ObservableCollection<Node>();
    }
}