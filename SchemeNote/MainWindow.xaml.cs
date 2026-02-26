using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SchemeNote.Models;
using SchemeNote.ViewModels;

namespace SchemeNote
{
    public partial class MainWindow : Window
    {
        // 1. 트리뷰 노드 드래그용 변수
        private Point _dragStartPoint;

        // 2. 그래프 무한 캔버스 줌/팬용 변수
        private Point _panStartPoint;
        private bool _isPanning = false;

        // 3. 물리 노드 드래그용 변수
        private VisualNode? _draggedNode = null;
        private bool _isNodeDragging = false;

        public MainWindow()
        {
            InitializeComponent();
        }

        #region TreeView Events

        private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.SelectedNode = e.NewValue as Node;
            }
        }

        private void TreeView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
        }

        private void TreeView_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
                return;

            Point currentPos = e.GetPosition(null);

            if (Math.Abs(currentPos.X - _dragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(currentPos.Y - _dragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                TreeView tree = sender as TreeView;
                if (tree?.SelectedItem is Node selectedNode)
                {
                    DragDrop.DoDragDrop(tree, selectedNode, DragDropEffects.Move);
                }
            }
        }

        private void TreeView_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(Node)))
                return;

            Node draggedNode = e.Data.GetData(typeof(Node)) as Node;

            DependencyObject target = (DependencyObject)e.OriginalSource;
            while (target != null && !(target is TreeViewItem))
            {
                target = VisualTreeHelper.GetParent(target);
            }

            if (target is TreeViewItem targetItem && targetItem.DataContext is Node targetNode)
            {
                if (draggedNode != null && draggedNode.Id != targetNode.Id)
                {
                    var vm = DataContext as MainViewModel;
                    vm?.MoveNode(draggedNode, targetNode);
                }
            }
        }

        #endregion

        #region Graph Canvas Events

        private void GraphCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            // 마우스 휠로 줌 인/아웃
            double zoomFactor = e.Delta > 0 ? 1.1 : 1 / 1.1;
            CanvasScale.ScaleX *= zoomFactor;
            CanvasScale.ScaleY *= zoomFactor;
        }

        private void GraphCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_isNodeDragging) return; // 노드를 잡고 있을 땐 화면 드래그 방지

            _isPanning = true;
            _panStartPoint = e.GetPosition(this);
            (sender as FrameworkElement)?.CaptureMouse();
        }

        private void GraphCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isPanning)
            {
                // 화면 드래그 (Pan) 이동 로직
                Point currentPoint = e.GetPosition(this);
                CanvasTranslate.X += currentPoint.X - _panStartPoint.X;
                CanvasTranslate.Y += currentPoint.Y - _panStartPoint.Y;
                _panStartPoint = currentPoint;
            }
        }

        private void GraphCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isPanning = false;
            (sender as FrameworkElement)?.ReleaseMouseCapture();
        }

        #endregion

        #region Node Events

        private void Node_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is VisualNode vNode)
            {
                _isNodeDragging = true;
                _draggedNode = vNode;
                _draggedNode.IsDragged = true; // 드래그 중엔 물리 위치 업데이트 중단

                (sender as FrameworkElement)?.CaptureMouse();
                e.Handled = true; // 캔버스 Pan 막기

                // 캔버스에서 노드 클릭 시 우측 속성창 업데이트
                if (DataContext is MainViewModel vm)
                {
                    vm.SelectedNode = vNode.Node;
                }
            }
        }

        private void Node_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isNodeDragging && _draggedNode != null)
            {
                // 현재 마우스 위치를 캔버스 기준 논리 좌표로 변환하여 적용
                Point mousePos = e.GetPosition(MainGraphCanvas);
                _draggedNode.X = mousePos.X;
                _draggedNode.Y = mousePos.Y;
                e.Handled = true;
            }
        }

        private void Node_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isNodeDragging && _draggedNode != null)
            {
                // 중앙 노드(Depth 0)가 아니면 손을 놨을 때 다시 물리엔진 영향을 받도록 풀어줌
                if (_draggedNode.Depth != 0)
                {
                    _draggedNode.IsDragged = false;
                }

                _isNodeDragging = false;
                _draggedNode = null;
                (sender as FrameworkElement)?.ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        private void Node_MouseEnter(object sender, MouseEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is VisualNode vNode)
            {
                vNode.IsHovered = true; // 반투명 호버 이펙트 On
            }
        }

        private void Node_MouseLeave(object sender, MouseEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is VisualNode vNode)
            {
                vNode.IsHovered = false; // 반투명 호버 이펙트 Off
            }
        }

        #endregion
    }
}