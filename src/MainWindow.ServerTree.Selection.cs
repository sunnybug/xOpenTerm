using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using xOpenTerm.Models;

namespace xOpenTerm;

/// <summary>主窗口：服务器树多选与键盘/鼠标交互。</summary>
public partial class MainWindow
{
    private bool _suppressTreeViewSelection;

    private void ServerTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindClickedNode(e.OriginalSource);
        var node = item?.Tag as Node;

        if (node == null) return;

        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        if (ctrl)
        {
            if (_selectedNodeIds.Contains(node.Id))
                _selectedNodeIds.Remove(node.Id);
            else
                _selectedNodeIds.Add(node.Id);
            if (_selectedNodeIds.Count == 0)
                _lastSelectedNodeId = null;
            else
                _lastSelectedNodeId = node.Id;
            e.Handled = true;
        }
        else if (shift)
        {
            var siblings = _nodes.Where(n => n.ParentId == node.ParentId).OrderBy(n => n.Name).ToList();
            var anchorNode = string.IsNullOrEmpty(_lastSelectedNodeId) ? null : _nodes.FirstOrDefault(n => n.Id == _lastSelectedNodeId);
            var anchorInSameLevel = anchorNode != null && anchorNode.ParentId == node.ParentId;

            _selectedNodeIds.Clear();
            if (anchorInSameLevel)
            {
                var idxClick = siblings.FindIndex(n => n.Id == node.Id);
                var idxAnchor = siblings.FindIndex(n => n.Id == _lastSelectedNodeId);
                if (idxClick >= 0 && idxAnchor >= 0)
                {
                    var (i0, i1) = idxClick <= idxAnchor ? (idxClick, idxAnchor) : (idxAnchor, idxClick);
                    for (var i = i0; i <= i1; i++)
                        AddNodeWithDescendants(siblings[i].Id);
                }
                else
                    _selectedNodeIds.Add(node.Id);
            }
            else
                _selectedNodeIds.Add(node.Id);

            _lastSelectedNodeId = node.Id;
            e.Handled = true;
        }
        else
        {
            _selectedNodeIds.Clear();
            _selectedNodeIds.Add(node.Id);
            _lastSelectedNodeId = node.Id;
        }

        if (item != null)
        {
            if (e.Handled)
            {
                item.Focus();
                if (node.Type == NodeType.group && IsClickOnItemHeader(item, e))
                    item.IsExpanded = !item.IsExpanded;
            }
            else
            {
                _suppressTreeViewSelection = true;
                item.IsSelected = true;
                _suppressTreeViewSelection = false;
            }
        }
        UpdateTreeSelectionVisuals();
    }

    private static bool IsClickOnItemHeader(TreeViewItem item, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(item);
        return pos.X >= 0 && pos.Y >= 0 && pos.X <= item.ActualWidth && pos.Y <= item.ActualHeight;
    }

    private void AddNodeWithDescendants(string nodeId)
    {
        _selectedNodeIds.Add(nodeId);
        foreach (var child in _nodes.Where(n => n.ParentId == nodeId))
            AddNodeWithDescendants(child.Id);
    }

    private List<Node> GetNodesInDisplayOrder()
    {
        var list = new List<Node>();
        void Add(Node n)
        {
            if (!MatchesSearch(n)) return;
            list.Add(n);
            foreach (var c in _nodes.Where(x => x.ParentId == n.Id).OrderBy(x => x.Name))
                Add(c);
        }
        foreach (var root in _nodes.Where(n => string.IsNullOrEmpty(n.ParentId)).OrderBy(n => n.Name))
            Add(root);
        return list;
    }

    private void UpdateTreeSelectionVisuals()
    {
        var selBg = (Brush)FindResource("SelectionBg");
        foreach (var tvi in EnumerateTreeViewItems(ServerTree))
        {
            if (tvi.Tag is Node n)
            {
                var selected = _selectedNodeIds.Contains(n.Id);
                SetIsMultiSelected(tvi, selected);
                tvi.Background = selected ? selBg : Brushes.Transparent;
            }
        }
    }
}
