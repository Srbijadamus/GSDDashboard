$cs = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=true;TrustServerCertificate=yes;"
$conn = New-Object System.Data.SqlClient.SqlConnection($cs)
$conn.Open()
$upd = 0
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='VWIC', SecondaryRole='Voice' WHERE EmployeeId='3193174'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='VWIC', SecondaryRole='Voice' WHERE EmployeeId='3193175'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='VWIC', SecondaryRole='Voice' WHERE EmployeeId='3193177'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='Voice', SecondaryRole='VWIC' WHERE EmployeeId='3193178'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='VWIC', SecondaryRole='Voice' WHERE EmployeeId='3193180'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='2nd Level', SecondaryRole='SSP' WHERE EmployeeId='4390187'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='2nd Level', SecondaryRole='SSP' WHERE EmployeeId='4390267'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='2nd Level', SecondaryRole='SSP' WHERE EmployeeId='4390487'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='SSP', SecondaryRole='Voice' WHERE EmployeeId='4451014'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='Voice', SecondaryRole='Voice' WHERE EmployeeId='4451020'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='Voice', SecondaryRole='VWIC' WHERE EmployeeId='4451022'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='Voice', SecondaryRole='VWIC' WHERE EmployeeId='4451025'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='Voice', SecondaryRole='WIC' WHERE EmployeeId='9044352'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='WIC', SecondaryRole='WIC' WHERE EmployeeId='9047339'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='SSP', SecondaryRole='SSP' WHERE EmployeeId='9074330'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='Voice', SecondaryRole='VWIC' WHERE EmployeeId='9074334'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='Voice', SecondaryRole='VWIC' WHERE EmployeeId='9074341'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='SSP', SecondaryRole='WIC' WHERE EmployeeId='9074345'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='Voice', SecondaryRole='Dispatcher' WHERE EmployeeId='9074348'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='SSP', SecondaryRole='Voice' WHERE EmployeeId='9074350'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='Bulk PWs', SecondaryRole='WIC' WHERE EmployeeId='9074352'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='Voice', SecondaryRole='Voice' WHERE EmployeeId='9074356'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='Voice', SecondaryRole='Voice' WHERE EmployeeId='9074363'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='Voice', SecondaryRole='Voice' WHERE EmployeeId='9074364'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='Voice', SecondaryRole='VWIC' WHERE EmployeeId='9074373'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='Voice', SecondaryRole='VWIC' WHERE EmployeeId='9074375'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='Voice', SecondaryRole='VWIC' WHERE EmployeeId='9074381'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='Voice', SecondaryRole='Voice' WHERE EmployeeId='9074428'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='WIC', SecondaryRole='SSP' WHERE EmployeeId='9074431'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='WIC', SecondaryRole='WIC' WHERE EmployeeId='9074512'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='SSP', SecondaryRole='SSP' WHERE EmployeeId='9074518'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='SME', SecondaryRole='SME' WHERE EmployeeId='9074519'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='Dispatcher', SecondaryRole='WIC' WHERE EmployeeId='9074526'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='Voice', SecondaryRole='Voice' WHERE EmployeeId='9074528'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='Voice', SecondaryRole='VWIC' WHERE EmployeeId='9074535'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='Voice', SecondaryRole='Voice' WHERE EmployeeId='9074543'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='Voice', SecondaryRole='Voice' WHERE EmployeeId='9074549'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='SSP', SecondaryRole='SSP' WHERE EmployeeId='9074557'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='Voice', SecondaryRole='Voice' WHERE EmployeeId='9074563'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='WIC', SecondaryRole='WIC' WHERE EmployeeId='9074573'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='Chat', SecondaryRole='Chat' WHERE EmployeeId='9074576'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='SSP', SecondaryRole='SSP' WHERE EmployeeId='9074582'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='Voice', SecondaryRole='Voice' WHERE EmployeeId='9074584'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='Voice', SecondaryRole='Voice' WHERE EmployeeId='9074590'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='SSP', SecondaryRole='VWIC' WHERE EmployeeId='9074592'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='Voice', SecondaryRole='Voice' WHERE EmployeeId='9074595'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='Voice', SecondaryRole='Voice' WHERE EmployeeId='9074611'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='Booking Tool', SecondaryRole='VWIC' WHERE EmployeeId='9075030'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='Voice', SecondaryRole='Voice' WHERE EmployeeId='9076905'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='SSP', SecondaryRole='VWIC' WHERE EmployeeId='9078602'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='Trainer', SecondaryRole='Voice' WHERE EmployeeId='9083024'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='WIC', SecondaryRole='SSP' WHERE EmployeeId='9084156'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='Voice', SecondaryRole='Voice' WHERE EmployeeId='9085121'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='Chat', SecondaryRole='VWIC' WHERE EmployeeId='9085123'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='SME', SecondaryRole='WIC' WHERE EmployeeId='9085138'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='Voice', SecondaryRole='Voice' WHERE EmployeeId='9086366'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='Voice', SecondaryRole='Voice' WHERE EmployeeId='9086658'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='Voice', SecondaryRole='Voice' WHERE EmployeeId='9087657'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='Voice', SecondaryRole='Voice' WHERE EmployeeId='9090511'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='Dispatcher', SecondaryRole='WIC' WHERE EmployeeId='9090513'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='Voice', SecondaryRole='Voice' WHERE EmployeeId='9090514'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='Voice', SecondaryRole='' WHERE EmployeeId='9092596'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='WIC', SecondaryRole='WIC' WHERE EmployeeId='9106144'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='WIC', SecondaryRole='SSP' WHERE EmployeeId='9107615'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='WIC', SecondaryRole='SSP' WHERE EmployeeId='9107616'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='WIC', SecondaryRole='WIC' WHERE EmployeeId='9112561'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='WIC', SecondaryRole='SSP' WHERE EmployeeId='9112563'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='Voice', SecondaryRole='Voice' WHERE EmployeeId='9114617'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='Voice', SecondaryRole='Voice' WHERE EmployeeId='9114618'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='WIC', SecondaryRole='WIC' WHERE EmployeeId='9117834'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='WIC', SecondaryRole='SSP' WHERE EmployeeId='9117836'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='WIC', SecondaryRole='WIC' WHERE EmployeeId='9118230'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='Voice', SecondaryRole='Voice' WHERE EmployeeId='9119463'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='WIC', SecondaryRole='SSP' WHERE EmployeeId='9120965'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='WIC', SecondaryRole='WIC' WHERE EmployeeId='9120969'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='VWIC', SecondaryRole='' WHERE EmployeeId='9120970'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='WIC', SecondaryRole='WIC' WHERE EmployeeId='9120971'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='WIC', SecondaryRole='SSP' WHERE EmployeeId='9120980'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='WIC', SecondaryRole='SSP' WHERE EmployeeId='9121951'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='WIC', SecondaryRole='WIC' WHERE EmployeeId='9122674'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='WIC', SecondaryRole='WIC' WHERE EmployeeId='9122675'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='WIC', SecondaryRole='WIC' WHERE EmployeeId='9122676'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='WIC', SecondaryRole='SSP' WHERE EmployeeId='9122679'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='WIC', SecondaryRole='WIC' WHERE EmployeeId='9124144'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='WIC', SecondaryRole='WIC' WHERE EmployeeId='9124145'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='WIC', SecondaryRole='WIC' WHERE EmployeeId='9124147'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='WIC', SecondaryRole='SSP' WHERE EmployeeId='9124148'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='Voice', SecondaryRole='Voice' WHERE EmployeeId='9124687'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='WIC', SecondaryRole='SSP' WHERE EmployeeId='9124688'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='Voice', SecondaryRole='Voice' WHERE EmployeeId='9124690'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='WIC', SecondaryRole='SSP' WHERE EmployeeId='9124691'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='Voice', SecondaryRole='Voice' WHERE EmployeeId='9124695'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='WIC', SecondaryRole='WIC' WHERE EmployeeId='9124697'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='WIC', SecondaryRole='WIC' WHERE EmployeeId='9125516'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='WIC', SecondaryRole='WIC' WHERE EmployeeId='9125517'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='WIC', SecondaryRole='SSP' WHERE EmployeeId='9125518'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='WIC', SecondaryRole='WIC' WHERE EmployeeId='9125519'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='WIC', SecondaryRole='WIC' WHERE EmployeeId='9125521'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='Voice', SecondaryRole='Voice' WHERE EmployeeId='9125526'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='WIC', SecondaryRole='WIC' WHERE EmployeeId='9126874'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='Chat CRO', SecondaryRole='VWIC' WHERE EmployeeId='9126877'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='WIC', SecondaryRole='' WHERE EmployeeId='9126878'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='WIC', SecondaryRole='' WHERE EmployeeId='9126880'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='WIC', SecondaryRole='WIC' WHERE EmployeeId='9126881'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='WIC', SecondaryRole='WIC' WHERE EmployeeId='9126882'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='WIC', SecondaryRole='WIC' WHERE EmployeeId='9126883'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='WIC', SecondaryRole='WIC' WHERE EmployeeId='9126885'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='WIC', SecondaryRole='WIC' WHERE EmployeeId='9126886'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='Chat CRO', SecondaryRole='Voice' WHERE EmployeeId='9126887'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='WIC', SecondaryRole='WIC' WHERE EmployeeId='9128148'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='WIC', SecondaryRole='' WHERE EmployeeId='9128149'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='WIC', SecondaryRole='' WHERE EmployeeId='9128153'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='WIC', SecondaryRole='WIC' WHERE EmployeeId='9128157'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='Chat CRO', SecondaryRole='VWIC' WHERE EmployeeId='9128158'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='WIC', SecondaryRole='WIC' WHERE EmployeeId='9129427'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='Voice', SecondaryRole='WIC' WHERE EmployeeId='9132070'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='Voice', SecondaryRole='WIC' WHERE EmployeeId='9132851'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$cmd=$conn.CreateCommand();$cmd.CommandText="UPDATE Employees SET PrimaryRole='Chat', SecondaryRole='Chat' WHERE EmployeeId='9133999'";$r=$cmd.ExecuteNonQuery();if($r){$upd++}
$conn.Close()
Write-Host "Updated roles: $upd"
