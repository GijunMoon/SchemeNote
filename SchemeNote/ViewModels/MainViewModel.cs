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
        [ObservableProperty] private int depth;
        [ObservableProperty] private double nodeSize = 100;
        [ObservableProperty] private bool isHovered;

        public bool IsDragged = false;

        public void UpdateSize() => NodeSize = Math.Max(70, 130 - (Depth * 15));
    }

    public partial class VisualEdge : ObservableObject
    {
        public Relation Relation { get; set; }
        public VisualNode Source { get; set; }
        public VisualNode Target { get; set; }

        [ObservableProperty] private Geometry? pathData;
        [ObservableProperty] private PointCollection? arrowPoints;
        [ObservableProperty] private Brush strokeBrush = Brushes.Gray;
        [ObservableProperty] private double strokeThickness = 2;
        [ObservableProperty] private DoubleCollection? strokeDashArray;

        public void UpdatePath()
        {
            if (Source == null || Target == null) return;

            // 베지어 곡선 계산
            var start = new Point(Source.X, Source.Y);
            var end = new Point(Target.X, Target.Y);
            double midX = (start.X + end.X) / 2;
            var cp1 = new Point(midX, start.Y);
            var cp2 = new Point(midX, end.Y);
            var figure = new PathFigure { StartPoint = start, IsClosed = false };
            figure.Segments.Add(new BezierSegment(cp1, cp2, end, true));
            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            PathData = geometry;

            UpdateArrow(start, end);
        }

        private void UpdateArrow(Point start, Point end)
        {
            double angle = Math.Atan2(end.Y - start.Y, end.X - start.X);
            double arrowLength = 12;
            double arrowAngle = Math.PI / 6;
            var points = new PointCollection();
            points.Add(end);
            points.Add(new Point(end.X - arrowLength * Math.Cos(angle - arrowAngle), end.Y - arrowLength * Math.Sin(angle - arrowAngle)));
            points.Add(new Point(end.X - arrowLength * Math.Cos(angle + arrowAngle), end.Y - arrowLength * Math.Sin(angle + arrowAngle)));
            ArrowPoints = points;
        }
    }

    public enum RelationFilter { All, Support, Oppose }

    public partial class MainViewModel : ObservableObject
    {
        private readonly AppDbContext _context;
        private readonly Random _rnd = new();

        public ObservableCollection<Subject> Subjects { get; set; } = new();
        public ObservableCollection<Node> RootNodes { get; set; } = new();
        public ObservableCollection<NodeType> NodeTypes { get; set; } = new();
        public ObservableCollection<RelationType> RelationTypes { get; set; } = new();
        public ObservableCollection<Node> AvailableTargetNodes { get; set; } = new();
        public ObservableCollection<RelationDisplayModel> DisplayRelations { get; set; } = new();
        public ObservableCollection<VisualNode> VisualNodes { get; set; } = new();
        public ObservableCollection<VisualEdge> VisualEdges { get; set; } = new();

        [ObservableProperty] private Subject? selectedSubject;
        [ObservableProperty] private Node? selectedNode;
        [ObservableProperty] private RelationType? selectedRelationType;
        [ObservableProperty] private Node? targetRelationNode;
        [ObservableProperty] private RelationFilter currentFilter = RelationFilter.All;

        partial void OnCurrentFilterChanged(RelationFilter value) => UpdateGraphVisualization();

        public MainViewModel()
        {
            _context = new AppDbContext();
            _context.Database.EnsureCreated();
            LoadInitialData();
        }

        private void LoadInitialData()
        {
            SeedDataIfEmpty();
            LoadSubjects();
            LoadNodeTypes();
            LoadRelationTypes();
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

        public void UpdateGraphVisualization()
        {
            VisualNodes.Clear();
            VisualEdges.Clear();
            if (SelectedNode == null) return;

            var visited = new Dictionary<Guid, VisualNode>();
            var queue = new Queue<(Node node, int depth)>();

            // 중심 노드 설정
            var centerVNode = new VisualNode { Node = SelectedNode, X = 0, Y = 0, Depth = 0 };
            centerVNode.UpdateSize();
            visited[SelectedNode.Id] = centerVNode;
            queue.Enqueue((SelectedNode, 0));

            double baseRadius = 400.0; // 반경 증가로 노드 간 거리 확대 (겹침 방지)

            while (queue.Count > 0)
            {
                var (currentNode, currentDepth) = queue.Dequeue();
                if (currentDepth >= 3) continue; // 최대 3단계까지만 표시

                // 현재 노드와 연결된 모든 관계 가져오기
                var relations = _context.Relations
                    .Include(r => r.RelationType)
                    .Where(r => r.FromNodeId == currentNode.Id || r.ToNodeId == currentNode.Id)
                    .ToList();

                // 필터링 적용
                if (CurrentFilter == RelationFilter.Support)
                    relations = relations.Where(r => r.RelationType?.Name.Contains("Support") == true || r.RelationType?.Name.Contains("보충") == true).ToList();
                else if (CurrentFilter == RelationFilter.Oppose)
                    relations = relations.Where(r => r.RelationType?.Name.Contains("Oppose") == true || r.RelationType?.Name.Contains("반대") == true).ToList();

                double angleStep = 2 * Math.PI / Math.Max(1, relations.Count);
                int index = 0;

                foreach (var rel in relations)
                {
                    Guid targetId = (rel.FromNodeId == currentNode.Id) ? rel.ToNodeId : rel.FromNodeId;

                    if (!visited.ContainsKey(targetId))
                    {
                        var targetNode = _context.Nodes.Include(n => n.NodeType).FirstOrDefault(n => n.Id == targetId);
                        if (targetNode != null)
                        {
                            double angle = index * angleStep + _rnd.NextDouble() * 0.5; // 랜덤 오프셋으로 겹침 방지
                            var vNode = new VisualNode
                            {
                                Node = targetNode,
                                Depth = currentDepth + 1,
                                X = baseRadius * (currentDepth + 1) * Math.Cos(angle),
                                Y = baseRadius * (currentDepth + 1) * Math.Sin(angle)
                            };
                            vNode.UpdateSize();
                            visited[targetId] = vNode;
                            queue.Enqueue((targetNode, currentDepth + 1));
                            index++;
                        }
                    }

                    // 엣지 추가 (중복 방지)
                    if (visited.ContainsKey(rel.FromNodeId) && visited.ContainsKey(rel.ToNodeId))
                    {
                        if (!VisualEdges.Any(e => e.Relation.Id == rel.Id))
                        {
                            var edge = new VisualEdge
                            {
                                Relation = rel,
                                Source = visited[rel.FromNodeId],
                                Target = visited[rel.ToNodeId],
                                StrokeBrush = (rel.RelationType?.Name.Contains("Oppose") == true || rel.RelationType?.Name.Contains("반대") == true) ? Brushes.Crimson : Brushes.DodgerBlue,
                                StrokeDashArray = (rel.RelationType?.Name.Contains("Oppose") == true || rel.RelationType?.Name.Contains("반대") == true) ? new DoubleCollection { 4, 4 } : null // Oppose에 점선 추가
                            };
                            edge.UpdatePath(); // 추가 전에 UpdatePath 호출 (초기 경로 설정)
                            VisualEdges.Add(edge);
                        }
                    }
                }
            }

            foreach (var vn in visited.Values) VisualNodes.Add(vn);
        }

        public void UpdateNodePosition(VisualNode vNode, double deltaX, double deltaY)
        {
            vNode.X += deltaX;
            vNode.Y += deltaY;

            // 이동된 노드와 연결된 모든 선 갱신
            var relatedEdges = VisualEdges.Where(e => e.Source == vNode || e.Target == vNode);
            foreach (var edge in relatedEdges)
            {
                edge.UpdatePath();
            }
        }

        private void SeedDataIfEmpty()
        {
            if (!_context.NodeTypes.Any())
            {
                _context.NodeTypes.AddRange(
                    new NodeType { Name = "Concept", ColorCode = "#4A90E2" },
                    new NodeType { Name = "Evidence", ColorCode = "#27AE60" },
                    new NodeType { Name = "Claim", ColorCode = "#E74C3C" }
                );
            }

            if (!_context.RelationTypes.Any())
            {
                _context.RelationTypes.AddRange(
                    new RelationType { Name = "보충 (Support)", Description = "뒷받침" },
                    new RelationType { Name = "반대 (Oppose)", Description = "반박" },
                    new RelationType { Name = "Type of", Description = "도형관계" }, // 이미지 기반 추가
                    new RelationType { Name = "Instance of", Description = "개체관계" },
                    new RelationType { Name = "Part of", Description = "부분 관계" },
                    new RelationType { Name = "Cause-Effect", Description = "인과 관계" },
                    new RelationType { Name = "Data-Claim", Description = "논증 관계" }
                );
            }

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