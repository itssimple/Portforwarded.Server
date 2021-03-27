# Portforwarded.Server

Simple software to help out with port forwarding

## Minecraft (Java) example

```powershell
# Windows
./Portforwarded.Server executable:file="java" executable:workingdirectory="C:\minecraftserver" executable:parameters="-Xmx4096M -Xms4096M -jar server.jar nogui" upnp:0:LocalIPAddress="192.168.1.110" upnp:0:Protocol="Tcp" upnp:0:LocalPort=25565 upnp:0:PublicPort=25565
```

```bash
# Linux / macOS
./Portforwarded.Server executable:file="java" executable:workingdirectory="/minecraft/server" executable:parameters="-Xmx4096M -Xms4096M -jar server.jar nogui" upnp:0:LocalIPAddress="192.168.1.110" upnp:0:Protocol="Tcp" upnp:0:LocalPort=25565 upnp:0:PublicPort=25565
```


### Breakdown of the options

| Configuration key | Configuration value | Description |
|:------------------|:--------------------|:------------|
| `executable:file` | `java` | What executable to run |
| `executable:workingdirectory` | `/minecraft/server` | The directory that the process will be started from |
| `executable:parameters` | `-Xmx4096M -Xms4096M -jar server.jar nogui` | The arguments passed to the `executable:file` |
| `upnp:X:LocalIPAddress` | `192.168.1.110` | The IP-address for entry `X` |
| `upnp:X:Protocol` | `Tcp` | Either `Udp` or `Tcp` |
| `upnp:X:LocalPort` | `25565` | The port number on the IP address |
| `upnp:X:PublicPort` | `25565` | The public port available on the internet |

The `upnp:X`-options can be added multiple times with an array index (Like this `upnp:0:LocalIPAddress`, `upnp:1:LocalIPAddress`)

The easiest way to handle the settings is to have a file called `appsettings.json` in the same folder as this software.

The file can look like this

```json
{
    "executable": {
        "file": "java",
        "workingdirectory": "/minecraft/server",
        "parameters": "-Xmx4096M -Xms4096M -jar server.jar nogui"
    },
    "upnp": [
        {
            "LocalIPAddress": "192.168.1.110",
            "Protocol": "Tcp",
            "LocalPort": 25565,
            "PublicPort": 25565
        }
    ]
}
```