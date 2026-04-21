# NeoHub

A real-time web portal for monitoring and controlling DSC PowerSeries NEO alarm panels via the ITv2 (TLink) protocol. Built with Blazor Server, MudBlazor, and MediatR.

[![Build and Publish Docker Image](https://github.com/BrianHumlicek/NeoHub/actions/workflows/docker-publish.yml/badge.svg)](https://github.com/BrianHumlicek/NeoHub/actions/workflows/docker-publish.yml)

## 🐳 Container Images

| Registry | Image | Pull Command |
|---|---|---|
| GitHub Container Registry | [`ghcr.io/brianhumlicek/neohub`](https://ghcr.io/brianhumlicek/neohub) | `docker pull ghcr.io/brianhumlicek/NeoHub:latest` |
| Docker Hub | [`brianhumlicek/neohub`](https://hub.docker.com/r/brianhumlicek/neohub) | `docker pull brianhumlicek/NeoHub:latest` |

---

## 🚀 Quick Start

### Docker Compose (Recommended)

1. Clone the repository (or download the `docker-compose.yml`):

````````
git clone https://github.com/BrianHumlicek/NeoHub.git
cd NeoHub
````````

<details>
<summary>docker-compose.yml</summary>

````````yaml
version: '3.8'

services:
  neohub:
    image: ghcr.io/brianhumlicek/neohub:latest
    container_name: neohub
    ports:
      - "8080:5181"
      - "8443:8181"
      - "3072:3072"
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - ASPNETCORE_URLS=http://+:8080
      - EnableHttps=false
    volumes:
      - ./persist:/app/persist
    restart: unless-stopped
````````

</details>

### Docker Run

Alternatively, you can run the container directly using the Docker CLI:

````````
docker run -d --name neohub \
  -p 8080:5181 \
  -p 8443:8181 \
  -p 3072:3072 \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -e ASPNETCORE_URLS=http://+:8080 \
  -e EnableHttps=false \
  -v ./persist:/app/persist \
  ghcr.io/brianhumlicek/neohub:latest
````````

### Getting Started

2. Start the container:

````````
docker-compose up -d
````````

3. Verify the UI is accessible at `http://localhost:8080` — you should see the Connections page:

   ![NeoHub Connections page before configuration](docs/images/connections-no-config.jpg)

4. Gather the information needed for panel programming (see [Before You Begin](#before-you-begin))

---

## 🔧 DSC Panel Programming

Before configuring NeoHub, your DSC PowerSeries NEO panel must be programmed to connect to your NeoHub server. All programming is done in **Installer Mode**.

### Before You Begin

Before opening the panel keypad, gather the following information and have something to write with — you will need to record values from the panel during programming.

#### What you need to know

- [ ] **Installer Code** — The default is `5555`. If that doesn't work, contact whoever originally commissioned the panel. A factory reset will restore the default, but will erase all existing programming and may disrupt any monitoring services currently connected to the panel.
- [ ] **NeoHub server IP address** — The IP address of the machine running NeoHub, reachable from the panel's network.
- [ ] **NeoHub listen port** — Default is `3072`. Only needed if you changed it.
- [ ] **Existing integrations** — Determine if any integration slots are already in use (e.g., Alarm.com, NEO Go, or other monitoring services). You will need to choose an unused slot to avoid disrupting existing services. If you are unsure, your monitoring provider or installer can tell you.
- [ ] **Firmware version** — The instructions below are written for **firmware v5.0+**. Communicator boards with older firmware (e.g., v4.1) use different subsection numbers and a different configuration process. If your panel has firmware prior to v5.0, see [Firmware v4.1 Configuration](#firmware-v41-configuration) instead.

#### What you will record from the panel

Have a place to write down these values — you will enter them into NeoHub's Settings page after programming:

- [ ] **Integration ID** (`[851][422]`) — A 12-digit read-only identifier unique to your panel
- [ ] **Type 2 Access Code** — The 32-character hex key you program for your chosen integration slot (only if you change it from the default)

### Entering Installer Mode

Once you have the information above and a way to write down settings from the panel, go to your alarm keypad.

1. **Enter Installer Configuration:** Press `*` `8` followed by your Installer Code (e.g., `*` `8` `5` `5` `5` `5` for the default)
2. **Navigate to the Alt Communicator section:** Enter `851`
3. **Read the Integration ID (`[422]`):**
   - Enter subsection `422`
   - The display will show the first 6 digits of your Integration ID — use the **right arrow** to scroll and reveal the remaining 6 digits (scrolling past the end will exit the subsection)
   - Write down all 12 digits and label it **Integration Identification Number `[851][422]`**
4. **Choose your integration group:**

   The panel supports **4 integration groups**, each allowing a separate integration server (e.g., Alarm.com, NEO Go, NeoHub) to be configured independently. All groups share the same Integration ID (`[422]`), but every other subsection has its own copy per group. The subsection numbers for each group are offset by 27:

   | Parameter               | Group 1 | Group 2 | Group 3 | Group 4 |
   |-------------------------|---------|---------|---------|---------|
   | Type 1 Access Code      |   `423` |   `450` |   `477` |   `504` |
   | Integration Options     |   `425` |   `452` |   `479` |   `506` |
   | Polling / Notifications |   `426` |   `453` |   `480` |   `507` |
   | Server IP Address       |   `428` |   `455` |   `482` |   `509` |
   | Server Port             |   `429` |   `456` |   `483` |   `510` |
   | Type 2 Access Code      |   `700` |   `701` |   `702` |   `703` |

   > ⚠️ **If you have a third-party service** (e.g., Alarm.com) already configured on your panel, it is using one of these groups. **Do not overwrite its settings.** Pick an unused group for NeoHub. If you are unsure which groups are in use, navigate to each group's Server IP subsection (e.g., `428`, `455`, `482`, `509`) — a non-zero IP address means that group is in use. Write down any existing values before making changes so you can restore them if needed.

   The remaining steps use **Group 1** subsection numbers. If you are using a different group, substitute the corresponding subsection number from the table above.

5. **Configure integration options (`[425,452,479,506]`):**
   - Enter subsection `425` (or the corresponding subsection for your group)
   - This controls encryption type and connection method for your integration slot
   - Ensure bits **3**, **4**, and **5** are set, and all other bits are clear
   - Toggle a bit by pressing its number on the keypad — the display should read `--345---` when correct
   - Press `#` to save and return to the subsection menu
6. **Configure polling and notification options (`[426,453,480,507]`):**
   - Enter subsection `426` (or the corresponding subsection for your group)
   - This controls integration polling method, real-time notification enablement, and notification port selection
   - Ensure only bit **3** is set — the display should read `--3-----`
   - Press `#` to save and return to the subsection menu
7. **Set the Integration Server IP address (`[428,455,482,509]`):**
   - Enter subsection `428` (or the corresponding subsection for your group)
   - Enter the IP address of your NeoHub server, 3 digits at a time, using the **right arrow** to advance to the next octet (e.g., for `192.168.1.100` enter `192` → ▶ → `168` → ▶ → `001` → ▶ → `100`)
   - Press `#` to save and return to the subsection menu
8. **Set the Integration Server port (`[429,456,483,510]`):**
   - Enter subsection `429` (or the corresponding subsection for your group)
   - This sets the port of the NeoHub server in hexadecimal — the default is `0C00` (3072 in decimal)
   - If you haven't changed the listen port in NeoHub, the default should already be correct
   - Press `#` to save and return to the subsection menu
9. **Set the Type 2 Access Code (`[700,701,702,703]`):**
    - Enter subsection `700` (or the corresponding subsection for your group)
    - This is a 32-character hex key used to encrypt traffic between the panel and NeoHub server
    - The factory default is `12345678123456781234567812345678` — NeoHub will connect automatically if this is left unchanged
    - The value is entered 8 characters at a time, using the **right arrow** to advance through all 32 characters
    - If you want to change it, be sure to write down the full 32-character value — you will need it when configuring the NeoHub server
    - Press `#` to save and return to the subsection menu
10. **Exit Installer Mode:** Press `*` `9` `9`

> ⚠️ **Critical:** The values you recorded from the panel (Integration ID and Type 2 Access Code) **must exactly match** what you enter into NeoHub's Settings page. If any value is wrong, the panel will not connect.

<details>
<summary><h3>Firmware v4.1 Configuration</h3></summary>

> ⚠️ This firmware version is still under testing. The subsection numbers differ from v5.0+.

| Subsection | Description | Notes |
|---|---|---|
| `[851][001]` | Panel Static IP address | |
| `[851][002]` | Panel Subnet mask | |
| `[851][003]` | Panel Gateway | |
| `[851][651]` | Panel Integration ID | Read-only |
| `[851][652]` | Access code | Default: `12345678` |
| `[851][664]` | Options | Keep only option 3 on |
| `[851][693]` | NeoHub server address | |
| `[851][697]` | DNS name | Clear this field (optional) |
| `[851][999]` | Restart | Enter `55` to restart |

**NeoHub settings for v4.1:**
- Only the **Type 1 Access Code** is needed (from `[851][652]`)
- Integration ID (from `[851][651]`)
- Server port

</details>

### Next Steps

Once the panel has been configured, return to the NeoHub web UI. If everything is working, you should see a new connection appear on the **Connections** page.

- **If you left the default access codes unchanged**, NeoHub will automatically connect and create settings for that connection — no further action is needed.
- **If you changed the Type 2 Access Code**, NeoHub will detect the connection but prompt you to update the access code. Navigate to the connection's settings, enter the Type 2 Access Code you recorded, and save. Once saved, the panel should connect successfully.

## 🏗️ Architecture

````````markdown
### Key Components

- **DSC.TLink** — Protocol library (ITv2 framing, encryption, session management)
- **MediatR** — Decouples panel messages from business logic via notification handlers
- **Notification Handlers** — Transform panel notifications into application state
- **State Services** — Singleton stores for partition/zone status
- **Event-Driven UI** — Blazor components subscribe to state change events (zero polling)

---

## 🌐 Web UI

| Page | Route | Description |
|---|---|---|
| **Home** | `/` | Dashboard showing all partitions and zones |
| **Zone Details** | `/zones/{sessionId}/{partition}` | Detailed view of zones for a specific partition |
| **Settings** | `/settings` | Edit application configuration (auto-saved to `userSettings.json`) |

### Features

- ✅ **Real-time updates** — UI refreshes instantly when zones open/close
- ✅ **Multi-session support** — Monitor multiple panels simultaneously
- ✅ **Visual zone indicators** — Color-coded chips with status icons
- ✅ **Event-driven architecture** — No polling, all updates triggered by actual panel events
- ✅ **Responsive design** — Works on desktop, tablet, and mobile

---

## 🔌 Ports

| Port | Protocol | Purpose |
|---|---|---|
| `5181` | HTTP | Web UI |
| `7013` | HTTPS | Web UI (secure) |
| `3072` | TCP | Panel ITv2 connection (configurable) |

---

## 🛠️ Building from Source

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### Local Development

````````

---

## 📖 Protocol Reference

For more details about the DSC ITv2 (TLink) protocol, see:
- [DSC TLink Library](https://github.com/BrianHumlicek/DSC-TLink) (underlying protocol implementation)
- DSC Integration Guide (consult your dealer/integrator documentation)

---

## 🐛 Troubleshooting

### Panel won't connect

1. Verify network connectivity: `ping <panel-ip>` and `ping <server-ip>` (from panel side if accessible)
2. Check firewall rules allow TCP port `3072` (or your configured `ListenPort`)
3. Verify panel is programmed with correct IP address and port
4. Check Docker logs: `docker logs NeoHub`

### "Waiting for partition status data..."

- The panel has connected but hasn't sent partition status yet
- This is normal on first connection — wait a few seconds
- Partition status is broadcast automatically by the panel every few minutes

### Encryption errors in logs

- Verify the Integration ID, Type 1, and Type 2 codes in `userSettings.json` **exactly match** the panel programming
- Type 2 code is case-sensitive hex (A-F must match)

### Check logs

````````

---

## 🤝 Contributing

Issues and pull requests are welcome! For major changes, please open an issue first to discuss the proposed changes.

---

## 🙏 Acknowledgements

Built with:
- [Blazor](https://dotnet.microsoft.com/apps/aspnet/web-apps/blazor) — .NET web framework
- [MudBlazor](https://mudblazor.com/) — Material Design component library
- [MediatR](https://github.com/jbogard/MediatR) — In-process messaging
- [DSC TLink](https://github.com/BrianHumlicek/DSC-TLink) — ITv2 protocol library
