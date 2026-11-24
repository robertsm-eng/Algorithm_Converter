Imports System.Collections.Generic
Imports System.Linq

' Shared, editable geometry: SVG and diagrams.net use the same node and edge coordinates.
Public Class FlowPoint
    Public ReadOnly Property X As Integer
    Public ReadOnly Property Y As Integer
    Public Sub New(xValue As Integer, yValue As Integer)
        X = xValue
        Y = yValue
    End Sub
End Class

Friend Class FlowRegion
    Public Property EntryId As String
    Public Property FollowingId As String
    Public Property Members As List(Of String)
End Class

Friend NotInheritable Class FlowLayout
    Private Const TrackSpacing As Integer = 24
    Private Const Margin As Integer = 30

    Private Class RoutePlan
        Public Property Edge As FlowEdge
        Public Property Source As FlowNode
        Public Property Target As FlowNode
        Public Property SourceRow As Integer
        Public Property TargetRow As Integer
        Public Property SourceCol As Integer
        Public Property TargetCol As Integer
        Public Property Channel As Integer = -1
        Public Property Track As Integer
        Public Property SourceChannel As Integer = -1
        Public Property SourceTrack As Integer
        Public Property OutTrack As Integer = -1
        Public Property InTrack As Integer = -1
        Public Property Direct As Boolean
        Public Property IsReturn As Boolean
    End Class

    Private Class Interval
        Public Property First As Integer
        Public Property Last As Integer
        Public Property Track As Integer
    End Class

    Private Sub New()
    End Sub

    Public Shared Sub SizeNode(n As FlowNode)
        Dim longest = n.Label.Replace(vbCr, "").Split(ChrW(10)).Max(Function(s) s.Length)
        Dim decision = n.Kind = "decision"
        n.Width = If(decision, Math.Max(320, Math.Min(640, longest * 15 + 60)), Math.Max(260, Math.Min(600, longest * 8 + 66)))
        If n.Kind = "terminal" Then n.Width = 230
        If n.Width Mod 2 <> 0 Then n.Width += 1
        Dim capacity = If(decision, (n.Width \ 2 - 26) \ 8, (n.Width - 64) \ 8)
        n.Lines = Wrap(n.Label, Math.Max(12, capacity))
        n.Height = If(decision, Math.Max(110, 2 * (n.Lines.Count * 18 + 20)), Math.Max(62, n.Lines.Count * 18 + 30))
        If n.Kind = "call" Then
            n.Lines = Wrap(n.Label, Math.Max(12, (n.Width - 2 * CInt(Math.Ceiling(n.Width * 0.07)) - 20) \ 8))
            n.Height = Math.Max(62, n.Lines.Count * 18 + 30)
        End If
    End Sub

    Private Shared Function Wrap(text As String, capacity As Integer) As List(Of String)
        Dim lines As New List(Of String)
        For Each raw In text.Replace(vbCr, "").Split(ChrW(10))
            Dim remaining = raw
            While remaining.Length > capacity
                Dim cut = remaining.LastIndexOf(" "c, capacity)
                If cut <= 0 Then cut = capacity
                ' Do not discard any characters from the actual label (stored separately).
                lines.Add(remaining.Substring(0, cut))
                remaining = remaining.Substring(cut).TrimStart()
            End While
            lines.Add(remaining)
        Next
        Return lines
    End Function

    Public Shared Sub Arrange(g As Flowchart, start As String)
        Dim nodes = g.Nodes.ToDictionary(Function(n) n.Id)
        Dim outgoing = g.Edges.GroupBy(Function(e) e.Source).ToDictionary(Function(gr) gr.Key, Function(gr) gr.ToList())
        Dim adjacency = nodes.Keys.ToDictionary(Function(id) id, Function(id) New List(Of String))
        For Each e In g.Edges.Where(Function(v) Not v.Back)
            If Not adjacency(e.Source).Contains(e.Target) Then adjacency(e.Source).Add(e.Target)
        Next
        Dim reachable As New HashSet(Of String) From {start}
        Dim queue As New Queue(Of String)
        queue.Enqueue(start)
        While queue.Count > 0
            For Each target In adjacency(queue.Dequeue())
                If reachable.Add(target) Then queue.Enqueue(target)
            Next
        End While

        ' A loop's continuation belongs below its complete body, not beside its first statement.
        ' These are layout-only constraints; no false executable edges are added.
        Dim preferredMain As New HashSet(Of FlowEdge)
        Dim rankedAdj = adjacency.ToDictionary(Function(kv) kv.Key, Function(kv) New List(Of String)(kv.Value))
        For Each region In g.Regions
            Dim members As New HashSet(Of String)(region.Members)
            If members.Contains(region.FollowingId) Then Continue For
            Dim exits = g.Edges.Where(Function(e) Not e.Back AndAlso e.Target = region.FollowingId AndAlso members.Contains(e.Source)).ToList()
            If exits.Count = 0 OrElse Not reachable.Contains(region.FollowingId) Then Continue For
            For Each e In exits
                preferredMain.Add(e)
            Next
            For Each id In members
                If reachable.Contains(id) AndAlso Not rankedAdj(id).Contains(region.FollowingId) Then rankedAdj(id).Add(region.FollowingId)
            Next
        Next
        Dim rows = Rank(start, reachable, rankedAdj)
        If rows.Count <> reachable.Count Then rows = Rank(start, reachable, adjacency)
        ' Defensive fallback for any unexpected cycle not marked by the parser.
        Dim lastRow = If(rows.Count = 0, -1, rows.Values.Max())
        For Each n In g.Nodes
            If Not rows.ContainsKey(n.Id) Then
                lastRow += 1
                rows(n.Id) = lastRow
            End If
        Next

        ' First branch keeps the parent's lane; secondary branches move right.
        ' A merge already assigned by the first branch keeps its lane.
        Dim cols As New Dictionary(Of String, Integer)
        Dim occupied As New HashSet(Of (Integer, Integer))
        Dim pending As New Stack(Of (Id As String, Col As Integer))
        pending.Push((start, 0))
        While pending.Count > 0
            Dim nextNode = pending.Pop()
            If cols.ContainsKey(nextNode.Id) Then Continue While
            Dim col = nextNode.Col
            While Not occupied.Add((rows(nextNode.Id), col))
                col += 1
            End While
            cols(nextNode.Id) = col
            Dim edges As List(Of FlowEdge) = Nothing
            If Not outgoing.TryGetValue(nextNode.Id, edges) Then Continue While
            Dim forward = edges.Where(Function(e) Not e.Back).ToList()
            For i = forward.Count - 1 To 0 Step -1
                Dim e = forward(i)
                Dim desired = If(i = 0 OrElse preferredMain.Contains(e), col, col + 1)
                pending.Push((e.Target, desired))
            Next
        End While
        For Each n In g.Nodes
            If Not cols.ContainsKey(n.Id) Then
                Dim col = 0
                While Not occupied.Add((rows(n.Id), col))
                    col += 1
                End While
                cols(n.Id) = col
            End If
        Next
        Dim rowCount = rows.Values.Max() + 1
        Dim colCount = cols.Values.Max() + 1
        Dim rowHeights(rowCount - 1) As Integer
        Dim colWidths(colCount - 1) As Integer
        For Each n In g.Nodes
            rowHeights(rows(n.Id)) = Math.Max(rowHeights(rows(n.Id)), n.Height)
            colWidths(cols(n.Id)) = Math.Max(colWidths(cols(n.Id)), n.Width)
        Next
        Dim gapTracks(rowCount) As Integer ' gap 0 above row 0, gap r+1 below row r
        Dim channels(colCount) As List(Of Interval) ' channel c is left of column c
        For i = 0 To colCount
            channels(i) = New List(Of Interval)
        Next
        Dim plans As New List(Of RoutePlan)
        For Each e In g.Edges
            Dim p As New RoutePlan With {.Edge = e, .Source = nodes(e.Source), .Target = nodes(e.Target),
                .SourceRow = rows(e.Source), .TargetRow = rows(e.Target), .SourceCol = cols(e.Source), .TargetCol = cols(e.Target)}
            p.IsReturn = e.Back OrElse p.TargetRow <= p.SourceRow
            Dim siblings = outgoing(e.Source)
            Dim isSecondary = siblings.Count > 1 AndAlso siblings(0) IsNot e
            If p.IsReturn Then
                e.TargetPort = "left"
                ' Use a separate channel for each overlapping return. A self-loop starts below its node.
                Dim firstReturns = siblings(0).Back OrElse rows(siblings(0).Target) <= p.SourceRow
                e.SourcePort = If(e.Source = e.Target, "bottom", If(isSecondary AndAlso firstReturns, "right", "left"))
                p.Channel = p.TargetCol
                p.Track = Reserve(channels(p.Channel), p.TargetRow, p.SourceRow + 1)
                If e.SourcePort <> "left" OrElse p.SourceCol <> p.TargetCol Then
                    p.OutTrack = TakeGap(gapTracks, p.SourceRow + 1)
                    If e.SourcePort <> "bottom" Then
                        p.SourceChannel = p.SourceCol + If(e.SourcePort = "right", 1, 0)
                        p.SourceTrack = Reserve(channels(p.SourceChannel), p.SourceRow, p.SourceRow + 1)
                    End If
                End If
            Else
                e.SourcePort = If(isSecondary AndAlso p.Source.Kind = "decision", "right", "bottom")
                e.TargetPort = "top"
                ' Adjacent nodes on the same lane need no detour.
                p.Direct = e.SourcePort = "bottom" AndAlso p.SourceCol = p.TargetCol AndAlso p.TargetRow = p.SourceRow + 1
                If Not p.Direct Then
                    p.Channel = If(e.SourcePort = "right" OrElse p.TargetCol >= p.SourceCol, p.SourceCol + 1, p.SourceCol)
                    p.Track = Reserve(channels(p.Channel), p.SourceRow, p.TargetRow)
                    If e.SourcePort = "bottom" Then p.OutTrack = TakeGap(gapTracks, p.SourceRow + 1)
                    p.InTrack = TakeGap(gapTracks, p.TargetRow)
                End If
            End If
            plans.Add(p)
        Next

        Dim channelLeft(colCount) As Integer
        Dim colLeft(colCount - 1) As Integer
        Dim x = Margin
        For c = 0 To colCount
            channelLeft(c) = x
            Dim trackCount = If(channels(c).Count = 0, 0, channels(c).Max(Function(v) v.Track) + 1)
            x += Math.Max(52, (trackCount + 1) * TrackSpacing + 20)
            If c < colCount Then
                colLeft(c) = x
                x += colWidths(c)
            End If
        Next
        Dim gapTop(rowCount) As Integer
        Dim rowTop(rowCount - 1) As Integer
        Dim y = Margin
        For r = 0 To rowCount
            gapTop(r) = y
            y += Math.Max(64, (gapTracks(r) + 1) * TrackSpacing + 16)
            If r < rowCount Then
                rowTop(r) = y
                y += rowHeights(r)
            End If
        Next
        For Each n In g.Nodes
            Dim c = cols(n.Id), r = rows(n.Id)
            n.X = colLeft(c) + (colWidths(c) - n.Width) \ 2
            n.Y = rowTop(r) + (rowHeights(r) - n.Height) \ 2
        Next
        For Each p In plans
            BuildRoute(p, channelLeft, gapTop)
        Next
    End Sub

    Private Shared Function Rank(start As String, reachable As HashSet(Of String), adjacency As Dictionary(Of String, List(Of String))) As Dictionary(Of String, Integer)
        Dim incoming = reachable.ToDictionary(Function(id) id, Function(id) 0)
        For Each id In reachable
            For Each target In adjacency(id)
                If reachable.Contains(target) Then incoming(target) += 1
            Next
        Next
        Dim queue As New Queue(Of String)(incoming.Where(Function(kv) kv.Value = 0).Select(Function(kv) kv.Key))
        Dim result As New Dictionary(Of String, Integer)
        Dim candidates As New Dictionary(Of String, Integer) From {{start, 0}}
        While queue.Count > 0
            Dim id = queue.Dequeue()
            Dim row As Integer
            candidates.TryGetValue(id, row)
            result(id) = row
            For Each target In adjacency(id)
                If Not incoming.ContainsKey(target) Then Continue For
                Dim previous As Integer
                candidates.TryGetValue(target, previous)
                candidates(target) = Math.Max(previous, row + 1)
                incoming(target) -= 1
                If incoming(target) = 0 Then queue.Enqueue(target)
            Next
        End While
        Return result
    End Function

    Private Shared Function Reserve(intervals As List(Of Interval), a As Integer, b As Integer) As Integer
        Dim first = Math.Min(a, b), last = Math.Max(a, b)
        Dim used As New HashSet(Of Integer)(intervals.Where(Function(v) v.First <= last AndAlso first <= v.Last).Select(Function(v) v.Track))
        Dim track = 0
        While used.Contains(track)
            track += 1
        End While
        intervals.Add(New Interval With {.First = first, .Last = last, .Track = track})
        Return track
    End Function

    Private Shared Function TakeGap(tracks As Integer(), gap As Integer) As Integer
        Dim track = tracks(gap)
        tracks(gap) += 1
        Return track
    End Function

    Private Shared Function Coordinate(origins As Integer(), index As Integer, track As Integer) As Integer
        Return origins(index) + 20 + track * TrackSpacing
    End Function

    Private Shared Function Port(n As FlowNode, side As String) As FlowPoint
        Select Case side
            Case "left"
                Return New FlowPoint(n.X + If(n.Kind = "io", 11, 0), n.Y + n.Height \ 2)
            Case "right"
                Return New FlowPoint(n.X + n.Width - If(n.Kind = "io", 11, 0), n.Y + n.Height \ 2)
            Case "top"
                Return New FlowPoint(n.X + n.Width \ 2, n.Y)
            Case Else
                Return New FlowPoint(n.X + n.Width \ 2, n.Y + n.Height)
        End Select
    End Function

    Public Shared Function PortStyle(side As String, source As Boolean) As String
        Dim prefix = If(source, "exit", "entry")
        Dim x = If(side = "left", "0", If(side = "right", "1", "0.5"))
        Dim y = If(side = "top", "0", If(side = "bottom", "1", "0.5"))
        Return prefix & "X=" & x & ";" & prefix & "Y=" & y & ";" & prefix & "Perimeter=1;"
    End Function

    Private Shared Sub BuildRoute(p As RoutePlan, channelLeft As Integer(), gapTop As Integer())
        Dim e = p.Edge
        Dim a = Port(p.Source, e.SourcePort), b = Port(p.Target, e.TargetPort)
        AddPoint(e, a.X, a.Y)
        If p.Direct Then
            AddPoint(e, b.X, b.Y)
        Else
            Dim lane = Coordinate(channelLeft, p.Channel, p.Track)
            If p.IsReturn Then
                If p.OutTrack >= 0 Then
                    Dim gap = Coordinate(gapTop, p.SourceRow + 1, p.OutTrack)
                    If e.SourcePort <> "bottom" Then
                        Dim sourceLane = Coordinate(channelLeft, p.SourceChannel, p.SourceTrack)
                        AddPoint(e, sourceLane, a.Y)
                        AddPoint(e, sourceLane, gap)
                    Else
                        AddPoint(e, a.X, gap)
                    End If
                    AddPoint(e, lane, gap)
                Else
                    AddPoint(e, lane, a.Y)
                End If
                AddPoint(e, lane, b.Y)
                AddPoint(e, b.X, b.Y)
            Else
                If p.OutTrack >= 0 Then
                    Dim gap = Coordinate(gapTop, p.SourceRow + 1, p.OutTrack)
                    AddPoint(e, a.X, gap)
                    AddPoint(e, lane, gap)
                Else
                    AddPoint(e, lane, a.Y)
                End If
                Dim entryGap = Coordinate(gapTop, p.TargetRow, p.InTrack)
                AddPoint(e, lane, entryGap)
                AddPoint(e, b.X, entryGap)
                AddPoint(e, b.X, b.Y)
            End If
        End If
        ' Labels belong close to the decision, not halfway around a return loop.
        If e.SourcePort = "left" Then
            e.LabelX = a.X - 30
            e.LabelY = a.Y - 8
        ElseIf e.SourcePort = "right" Then
            e.LabelX = a.X + 28
            e.LabelY = a.Y - 8
        Else
            e.LabelX = a.X + 21
            e.LabelY = a.Y + 17
        End If
    End Sub

    Private Shared Sub AddPoint(e As FlowEdge, x As Integer, y As Integer)
        If e.Points.Count > 0 Then
            Dim last = e.Points(e.Points.Count - 1)
            If last.X = x AndAlso last.Y = y Then Return
        End If
        e.Points.Add(New FlowPoint(x, y))
    End Sub
End Class
