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

1. Create a `docker-compose.yml` file:

````````
version: '3.4'

services:
  NeoHub:
    image: ghcr.io/brianhumlicek/NeoHub:latest
    container_name: NeoHub
    ports:
      - "5181:8080"     # HTTP Web UI
      - "7013:8443"     # HTTPS Web UI
      - "3072:3072"     # Panel ITv2 connection
    volumes:
      - NeoHub:/app/persist
    restart: unless-stopped

volumes:
  NeoHub_persist:
````````

2. Create a `userSettings.json` file (see [Configuration](#-configuration) below)

3. Start the container:

````````
docker-compose up -d
````````

4. Access the UI at `http://localhost:5181` or `https://localhost:7013`

### Docker Run

Alternatively, you can run the container directly using the Docker CLI:

````````
docker run -d --name NeoHub \
  -p 5181:8080 \
  -p 7013:8443 \
  -p 3072:3072 \
  -v ./userSettings.json:/app/userSettings.json \
  -v NeoHub_persist:/app/persist \
  ghcr.io/brianhumlicek/NeoHub:latest
````````

## 🔧 Configuration

NeoHub requires configuration to connect to your DSC panel. These settings can be configured in two ways:

1. **Via the Web UI** (Recommended): Navigate to the **Settings** page after startup
2. **Via JSON file**: Create/edit `userSettings.json` in the application directory

### Required Settings

The IntegrationIdentificationNumber and at least one of the access codes **must** be configured for NeoHub to work correctly: If you only use one access code, you must ensure the encryption type [851][425,452,479,506]bit4 is set for the corresponding access code type. If you are unsure about the encryption type, then make sure you have both access codes configured.

| Setting | Description | Format | Example |
|---|---|---|---|
| `IntegrationIdentificationNumber` | Integration ID from your panel (read from `[851][422]`) | 12 digits | `123456789012` |
| `IntegrationAccessCodeType1` | Type 1 encryption code (programmed in `[851][423,450,477,504]`) | 8 digits | `12345678` |
| `IntegrationAccessCodeType2` | Type 2 encryption key (programmed in `[851][700-703]`) | 32-character hex string | `1234...` (32 chars) |

### Optional Settings

| Setting | Description | Format | Default |
|---|---|---|---|
| `ListenPort` | TCP port for panel connections | 1-65535 | `3072` |

### Sample Configuration File

````````json
{
  "DSC.TLink": {
    "IntegrationIdentificationNumber": "123456789012",
    "IntegrationAccessCodeType1": "12345678",
    "IntegrationAccessCodeType2": "12345678123456781234567812345678",
    "ListenPort": 3072
  }
}
````````

> **💡 Tip:** The easiest way to configure NeoHub is via the **Settings** page in the web UI after startup. Changes are automatically saved to `userSettings.json`.

---

## 🔧 DSC Panel Programming

The DSC PowerSeries NEO panel must be programmed to connect to your NeoHub instance. All programming is done in **Installer Mode**.

### Entering Installer Mode

1. Enter `*8` on the panel keypad
2. Enter your **Installer Code** (default is usually `5555`)
3. Navigate to section **[851]** (Integration / Alternate Communicator)

### Understanding Tags

DSC panels organize programming into **sections** and **tags**:

- **Sections** (e.g., `[851]`) are groups of related settings
- **Tags** (e.g., `[422]`, `[423]`) are individual fields within a section
- Some settings span multiple tags (e.g., Type 2 encryption uses tags `[700]` through `[703]`)

### Multiple Integration Support

DSC Communicators with **firmware v5.0+** support up to **4 separate integrations** running simultaneously. This allows you to use multiple services at the same time, such as:

- **Integration 1:** Professional monitoring (e.g., Alarm.com)
- **Integration 2:** Mobile app (e.g., NEO Go)
- **Integration 3:** NeoHub server
- **Integration 4:** Custom integration

#### Understanding Multi-Slot Parameters

When viewing DSC programming documentation, you'll notice that **many configuration parameters** have **4 tag numbers** listed in brackets. This indicates that each integration slot has its own separate configuration for that parameter. 

For example, when you see:
- **Type 1 Access Code `[851][423,450,477,504]`**

This means there are 4 separate Type 1 Access Code fields:
  - Integration 1: `[851][423]`
  - Integration 2: `[851][450]`
  - Integration 3: `[851][477]`
  - Integration 4: `[851][504]`

This pattern applies to **multiple configuration parameters** in section `[851]`, not just the access codes. Each integration can have its own unique values for various settings, allowing different services to coexist with different configurations.

> ⚠️ **Important:** Before programming NeoHub, determine which integration slots are already in use by existing services. Choose an available slot to avoid conflicts.

> 📖 **Note:** The **Integration ID** (`[851][422]`) is **read-only** and **shared across all integrations**. You must copy this value from your panel and enter it into NeoHub's settings.

#### Firmware Compatibility

- **Firmware v5.0+:** Supports 4 integration slots as described above
- **Firmware prior to v5.0:** Only supports **1 integration slot**, and some programming tag numbers are different

> 📚 **Legacy Firmware Reference:** If you have firmware older than v5.0, refer to the [NEO Go Installation Guide](https://github.com/BrianHumlicek/DSC-TLink) in the DSC.TLink repository for details on the different tag mappings for older firmware versions.

### Required Programming

> 📋 **Note:** This guide assumes **firmware v5.0+**. For older firmware, tag numbers may differ - see the [firmware compatibility](#firmware-compatibility) section above.

Program the following tags in section **[851]** for your chosen integration slot:

| Tag | Name | Description | Example |
|---|---|---|---|
| `[422]` | Integration ID (Read-Only) | 12-digit identifier shared by all integrations - **copy to NeoHub settings** | `123456789012` |
| `[423,450,477,504]` | Type 1 Access Code (per integration) | 8-digit code for your chosen integration slot | `12345678` |
| `[700,701,702,703]` | Type 2 Access Code (per integration) | 32-character hex key for your chosen integration slot | See programming steps |

> 💡 **Tip:** Each integration slot has additional configuration parameters beyond just the Type 1 Access Code. Consult your panel's programming guide or the DSC Integration manual for a complete list of per-integration settings.

### Programming Steps

1. **Enter Installer Mode:** `*8` → Installer Code → `[851]`

2. **Read Integration ID (`[422]`):**
   - Navigate to tag `[422]`
   - **Note:** This field is read-only and displays your panel's unique 12-digit Integration ID
   - Write down this value - you'll need to enter it in NeoHub's settings
   - Example: `123456789012`

3. **Select Your Integration Slot:**
   - Choose an available integration slot (1-4) that isn't already in use
   - Use `[423]` for Integration 1, `[450]` for Integration 2, `[477]` for Integration 3, or `[504]` for Integration 4

4. **Program Type 1 Access Code:**
   - Navigate to your chosen integration tag (e.g., `[423]` for Integration 1)
   - Enter your 8-digit Type 1 code (e.g., `12345678`)
   - Press `#` to save
   - **Important:** Remember which slot you used and the code you entered

5. **Program Type 2 Access Code (`[700]`-`[703]`):**
   - **Note:** Type 2 Access Code tags are specific to each integration slot
   - For your chosen integration slot, navigate to the appropriate starting tag
   - The 32-character hex key is split into 4 segments of 8 characters each
   - Navigate to tag `[700]` (adjust based on your integration slot)
   - Enter characters 1-8 of your 32-character hex key
   - Press `#` and navigate to `[701]`
   - Enter characters 9-16
   - Press `#` and navigate to `[702]`
   - Enter characters 17-24
   - Press `#` and navigate to `[703]`
   - Enter characters 25-32
   - Press `#` to save

6. **Configure Network Settings:**
   - Set the panel's IP address for the NeoHub server (section `[801]`)
   - Set the port to `3072` (or your configured `ListenPort`)

7. **Exit Installer Mode:** Press `*99`

> ⚠️ **Critical:** The values programmed in the panel **must exactly match** the values in your `userSettings.json` file.

---

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
