using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO.Packaging;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using SchemeNote.Data;
using SchemeNote.Models;

namespace SchemeNote.ViewModels
{
    public class RelationDisplayModel
    {
        public Guid RelationId { get; set; }
        public string TypeName { get; set; } = "";
        public string TargetNodeTitle { get; set; } = "";
        public Relation? RelationEntity { get; set; }
    }

    public partial class VisualNode : ObservableObject
    {
        public Node Node { get; set; }

        [ObservableProperty] private double x;
        [ObservableProperty] private double y;
        [ObservableProperty] private int depth; // N-Depth 표현용
        [ObservableProperty] private double nodeSize = 100;
        [ObservableProperty] private bool isHovered;

        // 물리 엔진용 변수
        public double vx, vy;
        public bool IsDragged = false;

        public void UpdateSize() => NodeSize = Math.Max(60, 120 - (Depth * 20));
    }

    public partial class VisualEdge : ObservableObject
    {
        public Relation Relation { get; set; }
        public VisualNode Source { get; set; }
        public VisualNode Target { get; set; }
        public string EdgeType => Relation.RelationType?.Name ?? "";

        [ObservableProperty] private string pathData = "";
        [ObservableProperty] private PointCollection arrowPoints = new();
        [ObservableProperty] private Brush strokeBrush = Brushes.Gray;
        [ObservableProperty] private double strokeThickness = 2;
        [ObservableProperty] private DoubleCollection? strokeDashArray = null;

        public void UpdateGeometry()
        {
            if (EdgeType.Equals("Support", StringComparison.OrdinalIgnoreCase) || EdgeType.Contains("보충"))
            {
                StrokeBrush = Brushes.DodgerBlue;
                StrokeThickness = 3;
                StrokeDashArray = null;
            }
            else if (EdgeType.Equals("Oppose", StringComparison.OrdinalIgnoreCase) || EdgeType.Contains("반대"))
            {
                StrokeBrush = Brushes.Crimson;
                StrokeThickness = 2;
                StrokeDashArray = new DoubleCollection { 4, 4 };
            }

            double dx = Target.X - Source.X;
            double dy = Target.Y - Source.Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);

            if (dist == 0) return; // 0으로 나누기 방지

            double r1 = Source.NodeSize / 2;
            double r2 = Target.NodeSize / 2 + 10;

            double startX = Source.X + (dx / dist) * r1;
            double startY = Source.Y + (dy / dist) * r1;
            double endX = Target.X - (dx / dist) * r2;
            double endY = Target.Y - (dy / dist) * r2;

            double cx = (startX + endX) / 2 - dy * 0.2;
            double cy = (startY + endY) / 2 + dx * 0.2;

            PathData = $"M {startX:F1},{startY:F1} Q {cx:F1},{cy:F1} {endX:F1},{endY:F1}";

            double angle = Math.Atan2(endY - cy, endX - cx);
            Point p1 = new Point(endX, endY);
            Point p2 = new Point(endX - 15 * Math.Cos(angle - Math.PI / 6), endY - 15 * Math.Sin(angle - Math.PI / 6));
            Point p3 = new Point(endX - 15 * Math.Cos(angle + Math.PI / 6), endY - 15 * Math.Sin(angle + Math.PI / 6));

            ArrowPoints = new PointCollection { p1, p2, p3 };
        }
    }

    public enum RelationFilter { All, Support, Oppose }

    public partial class MainViewModel : ObservableObject
    {
        private readonly AppDbContext _context;

        public ObservableCollection<Subject> Subjects { get; set; } = new();
        public ObservableCollection<Node> RootNodes { get; set; } = new();
        public ObservableCollection<NodeType> NodeTypes { get; set; } = new();

        public ObservableCollection<RelationType> RelationTypes { get; set; } = new();
        public ObservableCollection<Node> AvailableTargetNodes { get; set; } = new();
        public ObservableCollection<RelationDisplayModel> DisplayRelations { get; set; } = new();

        public ObservableCollection<VisualNode> VisualNodes { get; set; } = new();
        public ObservableCollection<VisualEdge> VisualEdges { get; set; } = new();

        [ObservableProperty] private Subject? _selectedSubject;
        [ObservableProperty] private Node? _selectedNode;
        [ObservableProperty] private RelationType? _selectedRelationType;
        [ObservableProperty] private Node? _targetRelationNode;
        [ObservableProperty] private RelationFilter currentFilter = RelationFilter.All;

        partial void OnCurrentFilterChanged(RelationFilter value) => UpdateGraphVisualization();

        private DispatcherTimer _physicsTimer;
        private Random _rnd = new Random();

        public MainViewModel()
        {
            _context = new AppDbContext();
            _context.Database.EnsureCreated(); // 배포시 고려

            SeedNodeTypes();
            SeedRelationTypes();

            LoadSubjects();
            LoadNodeTypes();
            LoadRelationTypes();

            _physicsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _physicsTimer.Tick += PhysicsLoop;
            _physicsTimer.Start();
        }

        [RelayCommand]
        private void SelectGraphNode(Node node)
        {
            SelectedNode = node;
        }

        partial void OnSelectedNodeChanged(Node? value)
        {
            UpdateAvailableTargetNodes();
            UpdateRelationsList();
            UpdateGraphVisualization();
        }

        // 2. 구버전 UpdateGraphVisualization 삭제 및 신버전 하나로 통일
        public void UpdateGraphVisualization()
        {
            VisualNodes.Clear();
            VisualEdges.Clear();

            if (SelectedNode == null) return;

            int maxDepth = 3;
            var queue = new Queue<(Node node, int depth)>();
            var visited = new Dictionary<Guid, VisualNode>();
            var edgesSet = new HashSet<Guid>();

            var centerVNode = new VisualNode { Node = SelectedNode, X = 0, Y = 0, Depth = 0 };
            centerVNode.UpdateSize();
            visited[SelectedNode.Id] = centerVNode;
            queue.Enqueue((SelectedNode, 0));

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (current.depth >= maxDepth) continue;

                var relations = _context.Relations
                    .Include(r => r.RelationType)
                    .Where(r => r.FromNodeId == current.node.Id || r.ToNodeId == current.node.Id)
                    .ToList();

                if (CurrentFilter == RelationFilter.Support)
                    relations = relations.Where(r => r.RelationType?.Name.Contains("Support") == true || r.RelationType?.Name.Contains("보충") == true).ToList();
                else if (CurrentFilter == RelationFilter.Oppose)
                    relations = relations.Where(r => r.RelationType?.Name.Contains("Oppose") == true || r.RelationType?.Name.Contains("반대") == true).ToList();

                foreach (var rel in relations)
                {
                    bool isOutgoing = rel.FromNodeId == current.node.Id;
                    Guid targetNodeId = isOutgoing ? rel.ToNodeId : rel.FromNodeId;

                    if (!visited.ContainsKey(targetNodeId))
                    {
                        var targetNode = _context.Nodes.Include(n => n.NodeType).FirstOrDefault(n => n.Id == targetNodeId);
                        if (targetNode != null)
                        {
                            var vNode = new VisualNode
                            {
                                Node = targetNode,
                                Depth = current.depth + 1,
                                X = current.depth * 50 * (Math.Cos(_rnd.NextDouble() * Math.PI * 2)),
                                Y = current.depth * 50 * (Math.Sin(_rnd.NextDouble() * Math.PI * 2))
                            };
                            vNode.UpdateSize();
                            visited[targetNodeId] = vNode;
                            queue.Enqueue((targetNode, current.depth + 1));
                        }
                    }

                    if (!edgesSet.Contains(rel.Id) && visited.ContainsKey(rel.FromNodeId) && visited.ContainsKey(rel.ToNodeId))
                    {
                        edgesSet.Add(rel.Id);
                        VisualEdges.Add(new VisualEdge
                        {
                            Relation = rel,
                            Source = visited[rel.FromNodeId],
                            Target = visited[rel.ToNodeId]
                        });
                    }
                }
            }

            foreach (var vn in visited.Values) VisualNodes.Add(vn);
            foreach (var ve in VisualEdges) ve.UpdateGeometry();

            centerVNode.IsDragged = true;
            centerVNode.X = 0; centerVNode.Y = 0;
        }

        private void PhysicsLoop(object sender, EventArgs e)
        {
            if (VisualNodes.Count <= 1) return;

            double k = 150.0;
            double damp = 0.85;

            for (int i = 0; i < VisualNodes.Count; i++)
            {
                for (int j = i + 1; j < VisualNodes.Count; j++)
                {
                    var n1 = VisualNodes[i];
                    var n2 = VisualNodes[j];
                    double dx = n1.X - n2.X;
                    double dy = n1.Y - n2.Y;
                    double distSq = dx * dx + dy * dy;
                    if (distSq == 0) { dx = _rnd.NextDouble(); dy = _rnd.NextDouble(); distSq = dx * dx + dy * dy; }

                    double force = (k * k) / distSq;
                    double fx = force * dx;
                    double fy = force * dy;

                    n1.vx += fx; n1.vy += fy;
                    n2.vx -= fx; n2.vy -= fy;
                }
            }

            foreach (var edge in VisualEdges)
            {
                double dx = edge.Target.X - edge.Source.X;
                double dy = edge.Target.Y - edge.Source.Y;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist == 0) dist = 0.1;

                double force = (dist * dist) / k * 0.05;
                double fx = force * (dx / dist);
                double fy = force * (dy / dist);

                edge.Source.vx += fx; edge.Source.vy += fy;
                edge.Target.vx -= fx; edge.Target.vy -= fy;
            }

            foreach (var node in VisualNodes)
            {
                if (!node.IsDragged)
                {
                    node.X += node.vx * 0.05;
                    node.Y += node.vy * 0.05;
                }
                node.vx *= damp;
                node.vy *= damp;
            }

            foreach (var edge in VisualEdges) edge.UpdateGeometry();
        }

        private void SeedNodeTypes()
        {
            if (_context.NodeTypes.Any()) return;

            _context.NodeTypes.AddRange(
                new NodeType { Name = "Concept", ColorCode = "#4A90E2" },
                new NodeType { Name = "Evidence", ColorCode = "#27AE60" },
                new NodeType { Name = "Claim", ColorCode = "#E74C3C" }
            );
            _context.SaveChanges();
        }

        private void SeedRelationTypes()
        {
            if (_context.RelationTypes.Any()) return;

            _context.RelationTypes.AddRange(
                new RelationType { Name = "보충 (Support)", Description = "주장이나 개념을 뒷받침합니다." },
                new RelationType { Name = "반대 (Oppose)", Description = "주장에 반박합니다." },
                new RelationType { Name = "제약 (Constraint)", Description = "조건이나 제약을 추가합니다." }
            );
            _context.SaveChanges();
        }

        private void LoadSubjects()
        {
            var subjects = _context.Subjects.ToList();
            Subjects.Clear();
            foreach (var s in subjects) Subjects.Add(s);
        }

        private void LoadNodeTypes()
        {
            var types = _context.NodeTypes.ToList();
            NodeTypes.Clear();
            foreach (var t in types) NodeTypes.Add(t);
        }

        private void LoadRelationTypes()
        {
            var types = _context.RelationTypes.ToList();
            RelationTypes.Clear();
            foreach (var t in types) RelationTypes.Add(t);
        }

        [RelayCommand]
        private void AddSubject()
        {
            var subject = new Subject { Name = "New Subject" };
            _context.Subjects.Add(subject);
            _context.SaveChanges();
            Subjects.Add(subject);
            SelectedSubject = subject;
        }

        [RelayCommand]
        private void SaveSubject()
        {
            if (SelectedSubject == null) return;
            _context.SaveChanges();
            LoadSubjects();
        }

        [RelayCommand]
        private void DeleteSubject()
        {
            if (SelectedSubject == null) return;

            var nodes = _context.Nodes.Where(n => n.SubjectId == SelectedSubject.Id);
            var relations = _context.Relations.Where(r => r.SubjectId == SelectedSubject.Id);

            _context.Relations.RemoveRange(relations);
            _context.Nodes.RemoveRange(nodes);
            _context.Subjects.Remove(SelectedSubject);

            _context.SaveChanges();

            SelectedSubject = null;
            RootNodes.Clear();
            LoadSubjects();
        }

        partial void OnSelectedSubjectChanged(Subject? value)
        {
            if (value != null)
            {
                LoadNodes(value.Id);
                SelectedNode = null;
            }
        }

        private void LoadNodes(Guid subjectId)
        {
            var nodes = _context.Nodes.Where(n => n.SubjectId == subjectId).ToList();
            RootNodes.Clear();

            var rootNodes = nodes.Where(n => n.ParentId == null);
            foreach (var node in rootNodes) RootNodes.Add(node);
        }

        private void UpdateAvailableTargetNodes()
        {
            AvailableTargetNodes.Clear();
            if (SelectedSubject == null || SelectedNode == null) return;

            var nodes = _context.Nodes
                .Where(n => n.SubjectId == SelectedSubject.Id && n.Id != SelectedNode.Id)
                .ToList();

            foreach (var n in nodes) AvailableTargetNodes.Add(n);

            TargetRelationNode = null;
            SelectedRelationType = null;
        }

        private void UpdateRelationsList()
        {
            DisplayRelations.Clear();
            if (SelectedNode == null) return;

            var relations = _context.Relations
                .Where(r => r.FromNodeId == SelectedNode.Id || r.ToNodeId == SelectedNode.Id)
                .ToList();

            foreach (var r in relations)
            {
                bool isOutgoing = r.FromNodeId == SelectedNode.Id;
                var targetId = isOutgoing ? r.ToNodeId : r.FromNodeId;

                var targetNode = _context.Nodes.Find(targetId);
                var relType = _context.RelationTypes.Find(r.RelationTypeId);

                if (targetNode != null && relType != null)
                {
                    string directionPrefix = isOutgoing ? "▶ " : "◀ ";
                    DisplayRelations.Add(new RelationDisplayModel
                    {
                        RelationId = r.Id,
                        TypeName = relType.Name,
                        TargetNodeTitle = directionPrefix + targetNode.Title,
                        RelationEntity = r
                    });
                }
            }
        }

        [RelayCommand]
        private void AddRootNode()
        {
            if (SelectedSubject == null) return;

            var node = new Node
            {
                Title = "New Root Node",
                Content = "",
                SubjectId = SelectedSubject.Id
            };

            _context.Nodes.Add(node);
            _context.SaveChanges();
            RootNodes.Add(node);
        }

        [RelayCommand]
        private void AddChildNode(Node? targetNode)
        {
            var parentNode = targetNode ?? SelectedNode;
            if (SelectedSubject == null || SelectedNode == null) return;

            var node = new Node
            {
                Title = "Child Node",
                SubjectId = SelectedSubject.Id,
                ParentId = SelectedNode.Id
            };

            _context.Nodes.Add(node);
            _context.SaveChanges();
            if (!SelectedNode.Children.Contains(node))
            {
                SelectedNode.Children.Add(node);
            }
        }

        [RelayCommand]
        private void SaveNodeChanges()
        {
            if (SelectedNode == null) return;
            _context.SaveChanges();

            if (SelectedSubject != null)
            {
                var currentSelectedId = SelectedNode.Id;
                LoadNodes(SelectedSubject.Id);
                SelectedNode = _context.Nodes.Find(currentSelectedId);
                UpdateGraphVisualization();
            }
        }

        [RelayCommand]
        private void DeleteNode(Node? targetNode)
        {
            var nodeToDelete = targetNode ?? SelectedNode;
            if (SelectedNode == null) return;
            var nodesToRemove = new List<Node>();

            // 1. 자신 및 모든 하위 노드들을 재귀적으로 수집
            FindAllDescendants(nodeToDelete, nodesToRemove);
            nodesToRemove.Add(nodeToDelete); // 자기 자신 포함

            var nodeIdsToRemove = nodesToRemove.Select(n => n.Id).ToList();

            // 2. 삭제될 노드들과 얽혀있는 모든 릴레이션(연결선) 찾아서 일괄 삭제
            var relationsToRemove = _context.Relations
                .Where(r => nodeIdsToRemove.Contains(r.FromNodeId) || nodeIdsToRemove.Contains(r.ToNodeId))
                .ToList();

            _context.Relations.RemoveRange(relationsToRemove);

            // 3. UI 트리 컬렉션(ObservableCollection)에서 제거
            if (nodeToDelete.ParentId == null)
            {
                RootNodes.Remove(nodeToDelete);
            }
            else
            {
                var parent = _context.Nodes
                    .Include(n => n.Children)
                    .FirstOrDefault(n => n.Id == nodeToDelete.ParentId);

                if (parent != null && parent.Children.Contains(nodeToDelete))
                {
                    parent.Children.Remove(nodeToDelete);
                }
            }

            // 4. DB에서 노드들 일괄 삭제
            _context.Nodes.RemoveRange(nodesToRemove);
            _context.SaveChanges();

            SelectedNode = null;
            UpdateRelationsList();
            UpdateGraphVisualization();
        }

        // 하위 노드를 재귀적으로 찾는 헬퍼 메서드 (DeleteNode 바로 아래에 추가)
        private void FindAllDescendants(Node node, List<Node> descendants)
        {
            if (node.Children == null || !node.Children.Any()) return;

            // ToList()를 호출하여 순회 중 컬렉션 변경 오류(InvalidOperationException) 방지
            foreach (var child in node.Children.ToList())
            {
                descendants.Add(child);
                FindAllDescendants(child, descendants); // 재귀 호출
            }
        }

        public void MoveNode(Node draggedNode, Node newParent)
        {
            if (draggedNode == null || newParent == null || draggedNode.Id == newParent.Id) return;
            if (IsDescendant(newParent, draggedNode)) return;

            if (draggedNode.ParentId == null)
            {
                RootNodes.Remove(draggedNode);
            }
            else if (draggedNode.Parent != null)
            {
                draggedNode.Parent.Children.Remove(draggedNode);
            }

            draggedNode.ParentId = newParent.Id;
            draggedNode.Parent = newParent;
            _context.SaveChanges();

            if (!newParent.Children.Contains(draggedNode))
            {
                newParent.Children.Add(draggedNode);
            }
        }

        private bool IsDescendant(Node node, Node potentialAncestor)
        {
            var current = node;
            while (current != null)
            {
                if (current.Id == potentialAncestor.Id) return true;
                if (current.ParentId == null) break;
                current = _context.Nodes.FirstOrDefault(n => n.Id == current.ParentId);
            }
            return false;
        }

        [RelayCommand]
        private void AddRelation()
        {
            if (SelectedNode == null || TargetRelationNode == null || SelectedRelationType == null) return;

            var relation = new Relation
            {
                FromNodeId = SelectedNode.Id,
                ToNodeId = TargetRelationNode.Id,
                RelationTypeId = SelectedRelationType.Id,
                SubjectId = SelectedSubject.Id
            };

            _context.Relations.Add(relation);
            _context.SaveChanges();

            UpdateRelationsList();
            UpdateGraphVisualization(); // 관계 추가 후 화면 갱신
        }

        [RelayCommand]
        private void DeleteRelation(Relation? relationEntity)
        {
            if (relationEntity == null) return;
            _context.Relations.Remove(relationEntity);
            _context.SaveChanges();
            UpdateRelationsList();
            UpdateGraphVisualization(); // 관계 삭제 후 화면 갱신
        }
    }
}