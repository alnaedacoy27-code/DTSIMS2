Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.Data
Imports System.Text.RegularExpressions
Imports MySql.Data.MySqlClient

Public Class SupplierCounts
    Public Property Suppliers As Integer
    Public Property Companies As Integer
End Class

''' <summary>
''' Suppliers. The same person, company and phone number may repeat (branches), and different
''' companies may share an address; only the same company at the same address is a duplicate.
''' </summary>
Public Module SupplierRepository

    Private Function EscapeLike(s As String) As String
        Return s.Replace("\", "\\").Replace("%", "\%").Replace("_", "\_")
    End Function

    Private Function Tidy(s As String) As String
        Return Regex.Replace(If(s, "").Trim(), "\s+", " ")
    End Function

    Private Function NullIfBlank(s As String) As Object
        If String.IsNullOrWhiteSpace(s) Then Return Nothing
        Return Tidy(s)
    End Function

    Public Function GetSuppliers(search As String) As DataTable
        Dim sql As String =
            "SELECT s.id, s.name, s.company_name, s.contact_number, s.company_address, s.email, s.created_at, " &
            "(SELECT COUNT(*) FROM suppliers x WHERE x.company_name = s.company_name) AS branches, " &
            "(SELECT COUNT(*) FROM deliveries d WHERE d.supplier_id = s.id) AS deliveries " &
            "FROM suppliers s WHERE 1 = 1 "

        Dim prm As New Dictionary(Of String, Object)()
        If Not String.IsNullOrWhiteSpace(search) Then
            sql &= "AND (s.name LIKE @s OR s.company_name LIKE @s OR s.contact_number LIKE @s " &
                   "OR s.company_address LIKE @s OR s.email LIKE @s) "
            prm("@s") = "%" & EscapeLike(search.Trim()) & "%"
        End If
        sql &= "ORDER BY s.company_name, s.company_address"
        Return Db.Table(sql, prm)
    End Function

    Public Function GetSupplier(id As Integer) As DataRow
        Dim dt As DataTable = Db.Table(
            "SELECT id, name, company_name, contact_number, company_address, email FROM suppliers WHERE id = @id",
            Db.Params("@id", id))
        If dt.Rows.Count = 0 Then Return Nothing
        Return dt.Rows(0)
    End Function

    Public Function GetCounts() As SupplierCounts
        Dim dt As DataTable = Db.Table("SELECT COUNT(*) AS n, COUNT(DISTINCT company_name) AS c FROM suppliers")
        Dim r As New SupplierCounts()
        If dt.Rows.Count > 0 Then
            r.Suppliers = Convert.ToInt32(dt.Rows(0)("n"))
            r.Companies = Convert.ToInt32(dt.Rows(0)("c"))
        End If
        Return r
    End Function

    ''' <summary>Duplicate = this company is already saved at this address.</summary>
    ''' <summary>
    ''' A contact number belongs to one person: the name already saved with this number under a DIFFERENT
    ''' contact person, or "" when the number is free or used by the same person (at other companies or branches).
    ''' </summary>
    Public Function NumberOwner(number As String, person As String, exceptId As Integer) As String
        If String.IsNullOrWhiteSpace(number) Then Return ""
        Dim o As Object = Db.Scalar(
            "SELECT name FROM suppliers WHERE contact_number = @t AND name <> @n AND id <> @id LIMIT 1",
            Db.Params("@t", number.Trim(), "@n", Tidy(person), "@id", exceptId))
        If o Is Nothing OrElse o Is DBNull.Value Then Return ""
        Return o.ToString()
    End Function

    ''' <summary>Duplicate = same company at this address; NumberTaken = the number is another person's.</summary>
    Public Function CreateSupplier(name As String, company As String, number As String, address As String, email As String) As SaveResult
        If NumberOwner(number, name, 0).Length > 0 Then Return SaveResult.NumberTaken
        Try
            Db.NonQuery(
                "INSERT INTO suppliers (name, company_name, contact_number, company_address, email) VALUES (@n, @c, @t, @a, @e)",
                Db.Params("@n", Tidy(name), "@c", Tidy(company), "@t", NullIfBlank(number), "@a", Tidy(address), "@e", NullIfBlank(email)))
            Return SaveResult.Ok
        Catch ex As MySqlException When ex.Number = 1062
            Return SaveResult.Duplicate
        End Try
    End Function

    Public Function UpdateSupplier(id As Integer, name As String, company As String, number As String, address As String, email As String) As SaveResult
        If NumberOwner(number, name, id).Length > 0 Then Return SaveResult.NumberTaken
        Try
            Dim rows As Integer = Db.NonQuery(
                "UPDATE suppliers SET name = @n, company_name = @c, contact_number = @t, company_address = @a, email = @e WHERE id = @id",
                Db.Params("@n", Tidy(name), "@c", Tidy(company), "@t", NullIfBlank(number), "@a", Tidy(address), "@e", NullIfBlank(email), "@id", id))
            Return If(rows = 1, SaveResult.Ok, SaveResult.NotFound)
        Catch ex As MySqlException When ex.Number = 1062
            Return SaveResult.Duplicate
        End Try
    End Function

    ''' <summary>A supplier that already has deliveries cannot be deleted (InUse).</summary>
    Public Function DeleteSupplier(id As Integer) As SaveResult
        Try
            Dim rows As Integer = Db.NonQuery("DELETE FROM suppliers WHERE id = @id", Db.Params("@id", id))
            Return If(rows = 1, SaveResult.Ok, SaveResult.NotFound)
        Catch ex As MySqlException When ex.Number = 1451
            Return SaveResult.InUse
        End Try
    End Function

End Module
