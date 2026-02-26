using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using SchemeNote.Models;

namespace SchemeNote.Data
{
    public class AppDbContext : DbContext
    {
        public DbSet<Subject> Subjects { get; set; }
        public DbSet<Node> Nodes { get; set; }
        public DbSet<Relation> Relations { get; set; }
        public DbSet<NodeType> NodeTypes { get; set; }
        public DbSet<RelationType> RelationTypes { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            var folder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var path = Path.Combine(folder, "SchemeNote");

            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);

            var dbPath = Path.Combine(path, "logic.db");

            options.UseSqlite($"Data Source={dbPath}");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Self-reference (Parent-Child)
            modelBuilder.Entity<Node>()
                .HasOne(n => n.Parent)
                .WithMany(n => n.Children)
                .HasForeignKey(n => n.ParentId)
                .OnDelete(DeleteBehavior.Restrict);

            // NodeType 관계
            modelBuilder.Entity<Node>()
                .HasOne(n => n.NodeType)
                .WithMany()
                .HasForeignKey(n => n.NodeTypeId)
                .OnDelete(DeleteBehavior.SetNull);
        }
    }
}
