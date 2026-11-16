Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.Data
Imports System.Globalization
Imports System.Text

''' <summary>
''' Builds the short "live calculation" under a figure's "?": the formula, then the same formula with the real
''' numbers of the period. Kept to a few lines: long lists show the biggest items and "+ N more".
''' </summary>
Public Module ReportCalc

    Private ReadOnly Inv As CultureInfo = CultureInfo.InvariantCulture
    Public Const MaxLines As Integer = 6

    ''' <summary>"₱1,234.00", and "-₱168.00" (the sign before the peso sign).</summary>
    Public Function P(d As Decimal) As String
        If d < 0D Then Return "-" & ReportFormat.PesoSign & (-d).ToString("N2", Inv)
        Return ReportFormat.PesoSign & d.ToString("N2", Inv)
    End Function

    ''' <summary>12 -> "12", 2.5 -> "2.5".</summary>
    Public Function N(d As Decimal) As String
        Return If(d = Math.Floor(d), d.ToString("N0", Inv), d.ToString("N2", Inv))
    End Function

    Public Function Pct(part As Decimal, whole As Decimal) As String
        If whole = 0D Then Return "0%"
        Return Math.Round(part / whole * 100D, 1).ToString("0.0", Inv) & "%"
    End Function

    ''' <summary>"1 receipt" / "3 receipts".</summary>
    Public Function Count(howMany As Decimal, one As String, many As String) As String
        Return N(howMany) & " " & If(howMany = 1D, one, many)
    End Function

    ''' <summary>Joins the lines; Nothing / empty lines are skipped.</summary>
    Public Function Lines(ParamArray parts As String()) As String
        Dim sb As New StringBuilder()
        For Each s As String In parts
            If String.IsNullOrEmpty(s) Then Continue For
            If sb.Length > 0 Then sb.Append(Environment.NewLine)
            sb.Append(s)
        Next
        Return sb.ToString()
    End Function

    ''' <summary>Up to MaxLines items as "  • text", then "  • + 3 more: ₱120.00" when there are more.</summary>
    Public Function Bullets(items As List(Of String), Optional restText As String = "") As String
        Dim sb As New StringBuilder()
        For i As Integer = 0 To Math.Min(items.Count, MaxLines) - 1
            If sb.Length > 0 Then sb.Append(Environment.NewLine)
            sb.Append("  • ").Append(items(i))
        Next
        If items.Count > MaxLines Then
            sb.Append(Environment.NewLine).Append("  • + ").Append(items.Count - MaxLines).Append(" more").Append(restText)
        End If
        Return sb.ToString()
    End Function

    Public Function Dec(o As Object) As Decimal
        If o Is Nothing OrElse o Is DBNull.Value Then Return 0D
        Return Convert.ToDecimal(o)
    End Function

    Public Function Txt(o As Object) As String
        If o Is Nothing OrElse o Is DBNull.Value Then Return ""
        Return Convert.ToString(o, Inv)
    End Function

    ''' <summary>"welding rod 6inch" (the size only when there is one).</summary>
    Public Function Item(name As Object, size As Object) As String
        Dim s As String = Txt(size).Trim()
        Return Txt(name) & If(s.Length = 0 OrElse s = "-", "", " " & s)
    End Function

End Module
