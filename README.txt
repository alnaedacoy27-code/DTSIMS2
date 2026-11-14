DTSIMS2 app - Step 4: database connection, first-run Superadmin, login

UPDATE STEPS
1. Close Visual Studio.
2. Extract this zip into C:\DTSIMS2\  (replace the files when asked)
3. Open C:\DTSIMS2\DTSIMS2-App\DTSIMS2.vbproj in Visual Studio
4. Right-click the project (DTSIMS2) > Manage NuGet Packages > Browse
   > search "MySql.Data" (by Oracle) > Install (accept the license)
5. Build > Rebuild Solution, then F5

WHAT HAPPENS WHEN IT STARTS
 a. Connects to dtsims2_db  (if it can't: the "Database connection" screen asks for the server)
 b. No Superadmin yet?       -> "Create the Superadmin" screen
 c. Login screen             -> main window
 Log out (Account menu)      -> back to the login screen

Connection settings are saved per Windows user in %APPDATA%\DTSIMS2\connection.cfg
(the password is encrypted). Defaults: localhost : 3306, dtsims2_db, darwin_server.
