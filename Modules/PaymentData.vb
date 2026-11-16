Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.Data
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging
Imports System.IO
Imports System.Text.RegularExpressions
Imports MySql.Data.MySqlClient

''' <summary>One way a customer can pay (Cash, GCash, Maya, a bank account ...).</summary>
Public Class PaymentMethod
    Public Property Id As Integer
    Public Property Name As String = ""
    ''' <summary>cash, ewallet, bank, card or other</summary>
    Public Property Kind As String = "ewallet"
    Public Property AccountName As String = ""
    Public Property AccountNumber As String = ""
    Public Property NeedsReference As Boolean
    Public Property HasQr As Boolean

    Public ReadOnly Property IsCash As Boolean
        Get
            Return Kind = "cash"
        End Get
    End Property

    Public Overrides Function ToString() As String
        Return Name
    End Function
End Class

''' <summary>
''' File Maintenance > Payment Methods, and the POS: which payment methods exist, their account and QR code.
''' Cash always exists and cannot be removed. A non-cash payment is checked by the cashier on the customer's
''' confirmation screen and its reference number is saved with the sale (a reference cannot be used twice).
''' </summary>
Public Module PaymentRepository

    Public Const MaxQrBytes As Long = 5L * 1024L * 1024L
    Private ReadOnly AccountNumberRule As New Regex("^[0-9][0-9 \-]{2,28}[0-9]$")
    Private ReadOnly ReferenceRule As New Regex("^[A-Za-z0-9][A-Za-z0-9\-]{3,39}$")

    Public ReadOnly KindCodes As String() = {"ewallet", "bank", "card", "other"}

    Public Function KindText(kind As String) As String
        Select Case kind
            Case "cash"
                Return "Cash"
            Case "ewallet"
                Return "E-wallet"
            Case "bank"
                Return "Bank transfer"
            Case "card"
                Return "Card"
            Case Else
                Return "Other"
        End Select
    End Function

    Public Function KindFromText(text As String) As String
        For Each k As String In KindCodes
            If KindText(k) = text Then Return k
        Next
        Return "other"
    End Function

    ''' <summary>"" when the account number is fine (empty is allowed), otherwise why not.</summary>
    Public Function AccountNumberProblem(value As String) As String
        Dim v As String = If(value, "").Trim()
        If v.Length = 0 Then Return ""
        If Not AccountNumberRule.IsMatch(v) Then Return "The account number can only have numbers, spaces and dashes (4 to 30 characters)."
        Return ""
    End Function

    ''' <summary>"" when the reference number is fine, otherwise why not.</summary>
    Public Function ReferenceProblem(value As String) As String
        Dim v As String = If(value, "").Trim()
        If v.Length = 0 Then Return "Type the reference number from the customer's payment confirmation."
        If Not ReferenceRule.IsMatch(v) Then Return "The reference number is 4 to 40 letters, numbers or dashes."
        Return ""
    End Function

    ''' <summary>"0917 123 4567" -> "•••• 4567" for screens where the full number is not needed.</summary>
    Public Function Masked(accountNumber As String) As String
        Dim digits As String = Regex.Replace(If(accountNumber, ""), "[^0-9]", "")
        If digits.Length <= 4 Then Return If(accountNumber, "")
        Return "•••• " & digits.Substring(digits.Length - 4)
    End Function

    ' ------------------------------------------------------------------ reading
    Private Function EscapeLike(s As String) As String
        Return s.Replace("\", "\\").Replace("%", "\%").Replace("_", "\_")
    End Function

    Public Function GetAll(search As String) As DataTable
        Dim sql As String =
            "SELECT p.id, p.name, p.kind, COALESCE(p.account_name, '') AS account_name, COALESCE(p.account_number, '') AS account_number, " &
            "p.needs_reference, p.is_active, (p.qr_image IS NOT NULL) AS has_qr, p.created_at, " &
            "(SELECT COUNT(*) FROM sales s WHERE s.payment_method_id = p.id) AS used_count " &
            "FROM payment_methods p WHERE 1 = 1 "
        Dim prm As New Dictionary(Of String, Object)()
        If Not String.IsNullOrWhiteSpace(search) Then
            sql &= "AND (p.name LIKE @s OR p.account_name LIKE @s OR p.account_number LIKE @s) "
            prm("@s") = "%" & EscapeLike(search.Trim()) & "%"
        End If
        sql &= "ORDER BY (p.kind = 'cash') DESC, p.is_active DESC, p.sort_order, p.name"
        Return Db.Table(sql, prm)
    End Function

    Public Function GetOne(id As Integer) As DataRow
        Dim dt As DataTable = Db.Table("SELECT id, name, kind, account_name, account_number, needs_reference, is_active, " &
                                       "(qr_image IS NOT NULL) AS has_qr FROM payment_methods WHERE id = @id", Db.Params("@id", id))
        If dt.Rows.Count = 0 Then Return Nothing
        Return dt.Rows(0)
    End Function

    ''' <summary>The methods the cashier can choose at the POS: Cash first, then the active ones.</summary>
    Public Function GetActive() As List(Of PaymentMethod)
        Dim l As New List(Of PaymentMethod)()
        For Each r As DataRow In Db.Table(
                "SELECT id, name, kind, COALESCE(account_name, '') AS account_name, COALESCE(account_number, '') AS account_number, " &
                "needs_reference, (qr_image IS NOT NULL) AS has_qr FROM payment_methods " &
                "WHERE is_active = 1 ORDER BY (kind = 'cash') DESC, sort_order, name").Rows
            Dim m As New PaymentMethod()
            m.Id = Convert.ToInt32(r("id"))
            m.Name = r("name").ToString()
            m.Kind = r("kind").ToString()
            m.AccountName = r("account_name").ToString()
            m.AccountNumber = r("account_number").ToString()
            m.NeedsReference = Convert.ToInt32(r("needs_reference")) <> 0
            m.HasQr = Convert.ToInt32(r("has_qr")) <> 0
            l.Add(m)
        Next
        Return l
    End Function

    Public Function GetQr(id As Integer) As Byte()
        Return TryCast(Db.Scalar("SELECT qr_image FROM payment_methods WHERE id = @id", Db.Params("@id", id)), Byte())
    End Function

    Public Function GetCounts() As DiscountCounts
        Dim dt As DataTable = Db.Table("SELECT COUNT(*) AS n, COALESCE(SUM(is_active = 1), 0) AS a FROM payment_methods")
        Dim c As New DiscountCounts()
        c.Total = Convert.ToInt32(dt.Rows(0)("n"))
        c.Active = Convert.ToInt32(dt.Rows(0)("a"))
        Return c
    End Function

    ' ------------------------------------------------------------------ writing
    ''' <summary>qr: Nothing = keep the saved one (edit) / none (add). clearQr removes it.</summary>
    Public Function Save(id As Integer, name As String, kind As String, accountName As String, accountNumber As String,
                         needsReference As Boolean, qr As Byte(), clearQr As Boolean) As SaveResult
        If String.IsNullOrWhiteSpace(name) OrElse name.Trim().Length > 60 Then Return SaveResult.Invalid
        If Array.IndexOf(KindCodes, kind) < 0 Then Return SaveResult.Invalid
        If AccountNumberProblem(accountNumber).Length > 0 Then Return SaveResult.Invalid
        Try
            If id = 0 Then
                Db.NonQuery("INSERT INTO payment_methods (name, kind, account_name, account_number, needs_reference, qr_image, sort_order) " &
                            "VALUES (@n, @k, @an, @ac, @r, @q, (SELECT x.m FROM (SELECT COALESCE(MAX(sort_order), 0) + 1 AS m FROM payment_methods) x))",
                            Db.Params("@n", name.Trim(), "@k", kind, "@an", NullIfBlank(accountName), "@ac", NullIfBlank(accountNumber),
                                      "@r", If(needsReference, 1, 0), "@q", qr))
            Else
                Dim sql As String = "UPDATE payment_methods SET name = @n, kind = @k, account_name = @an, account_number = @ac, needs_reference = @r" &
                                    If(clearQr, ", qr_image = NULL", If(qr IsNot Nothing, ", qr_image = @q", "")) &
                                    " WHERE id = @id AND kind <> 'cash'"
                If Db.NonQuery(sql, Db.Params("@n", name.Trim(), "@k", kind, "@an", NullIfBlank(accountName), "@ac", NullIfBlank(accountNumber),
                                              "@r", If(needsReference, 1, 0), "@q", qr, "@id", id)) = 0 Then Return SaveResult.NotFound
            End If
        Catch ex As MySqlException When ex.Number = 1062
            Return SaveResult.Duplicate
        End Try
        NotifyChanged()
        Return SaveResult.Ok
    End Function

    ''' <summary>Cash is always available.</summary>
    Public Function SetActive(id As Integer, active As Boolean) As SaveResult
        Dim n As Integer = Db.NonQuery("UPDATE payment_methods SET is_active = @a WHERE id = @id AND kind <> 'cash'",
                                       Db.Params("@a", If(active, 1, 0), "@id", id))
        If n = 0 Then Return SaveResult.NotFound
        NotifyChanged()
        Return SaveResult.Ok
    End Function

    ''' <summary>Past sales keep the method's name and the reference number, so deleting is safe. Cash cannot be deleted.</summary>
    Public Function Delete(id As Integer) As SaveResult
        Dim n As Integer = Db.NonQuery("DELETE FROM payment_methods WHERE id = @id AND kind <> 'cash'", Db.Params("@id", id))
        If n = 0 Then Return SaveResult.NotFound
        NotifyChanged()
        Return SaveResult.Ok
    End Function

    Private Function NullIfBlank(s As String) As Object
        If String.IsNullOrWhiteSpace(s) Then Return Nothing
        Return s.Trim()
    End Function

    ' payment_methods has no trigger (the app's database account cannot create triggers): tell the other laptops here
    Private Sub NotifyChanged()
        Try
            Db.NonQuery("INSERT INTO change_log (module) VALUES ('payment_methods')")
        Catch
            ' ignore
        End Try
    End Sub

    ' ------------------------------------------------------------------ QR picture
    ''' <summary>A picked QR picture as a PNG at most 600 px on its longest side (keeps the database light, stays sharp).</summary>
    Public Function ToQrBytes(path As String, ByRef errorMessage As String) As Byte()
        errorMessage = ""
        Try
            If New FileInfo(path).Length > MaxQrBytes Then
                errorMessage = "That picture is too large. Choose one under 5 MB."
                Return Nothing
            End If
            Using fs As New FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)
                Using src As Image = Image.FromStream(fs)
                    Dim scale As Double = Math.Min(1.0R, 600.0R / Math.Max(src.Width, src.Height))
                    Dim w As Integer = Math.Max(1, CInt(src.Width * scale))
                    Dim h As Integer = Math.Max(1, CInt(src.Height * scale))
                    Using bmp As New Bitmap(w, h)
                        Using g As Graphics = Graphics.FromImage(bmp)
                            g.Clear(Color.White)
                            ' nearest neighbour keeps the QR squares crisp when it is made smaller
                            g.InterpolationMode = If(scale < 1.0R, InterpolationMode.HighQualityBicubic, InterpolationMode.NearestNeighbor)
                            g.DrawImage(src, New Rectangle(0, 0, w, h))
                        End Using
                        Using ms As New MemoryStream()
                            bmp.Save(ms, ImageFormat.Png)
                            Return ms.ToArray()
                        End Using
                    End Using
                End Using
            End Using
        Catch ex As Exception
            errorMessage = "That file is not a picture this app can read."
            Return Nothing
        End Try
    End Function

    Public Function ToImage(bytes As Byte()) As Image
        If bytes Is Nothing OrElse bytes.Length = 0 Then Return Nothing
        Try
            Using ms As New MemoryStream(bytes)
                Using img As Image = Image.FromStream(ms)
                    Return New Bitmap(img)
                End Using
            End Using
        Catch
            Return Nothing
        End Try
    End Function

End Module
