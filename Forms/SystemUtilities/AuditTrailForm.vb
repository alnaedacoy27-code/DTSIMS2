Option Strict On
Option Explicit On

Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>
''' Utilities > Audit Trail (Superadmin only). A read-only record of every action in the system: who did what,
''' where and when. Daily / Weekly / Monthly, search, Save PDF, Save Excel and Print. There is no way to edit or
''' delete an entry here. A new entry shows up by itself within about 10 seconds.
''' </summary>
<System.ComponentModel.DesignerCategory("Code")>
Public Class AuditTrailForm
    Inherits ReportFormBase

    Private ReadOnly _watch As New System.Windows.Forms.Timer()
    Private _lastId As Long = -1

    Public Sub New()
        MyBase.New()
        _watch.Interval = 10000
        AddHandler _watch.Tick, AddressOf OnWatchTick
    End Sub

    Protected Overrides ReadOnly Property ReportTitle As String
        Get
            Return "Audit Trail"
        End Get
    End Property

    Protected Overrides ReadOnly Property ReportBlurb As String
        Get
            Return "A record of every action in the system"
        End Get
    End Property

    Protected Overrides ReadOnly Property ViewNames As String()
        Get
            Return New String() {"Activity log", "Summary", "By person"}
        End Get
    End Property

    Public Overrides ReadOnly Property WatchedTables As String()
        Get
            Return New String() {"audit_trail"}
        End Get
    End Property

    Protected Overrides Function BuildDoc(view As Integer, range As DateRange, period As ReportPeriod) As ReportDoc
        Select Case view
            Case 1
                Return AuditData.Summary(period, range)
            Case 2
                Return AuditData.ByPerson(period, range)
            Case Else
                Return AuditData.ActivityLog(period, range)
        End Select
    End Function

    Protected Overrides Function SearchEnabled(view As Integer) As Boolean
        Return True
    End Function

    ' ------------------------------------------------------------------ new entries appear by themselves
    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        _watch.Start()
    End Sub

    Private Sub OnWatchTick(sender As Object, e As EventArgs)
        If IsDisposed OrElse Not IsHandleCreated Then Return
        Task.Run(Function() AuditData.LatestId()).ContinueWith(
            Sub(t As Task(Of Long))
                If t.IsFaulted OrElse t.IsCanceled Then Return
                Dim id As Long = t.Result
                Try
                    If IsDisposed OrElse Not IsHandleCreated Then Return
                    BeginInvoke(New Action(Sub() OnLatestId(id)))
                Catch ex As InvalidOperationException
                    ' the screen was closed meanwhile
                End Try
            End Sub)
    End Sub

    Private Sub OnLatestId(id As Long)
        If _lastId >= 0 AndAlso id <> _lastId Then RefreshData()
        _lastId = id
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            _watch.Stop()
            _watch.Dispose()
        End If
        MyBase.Dispose(disposing)
    End Sub

End Class
