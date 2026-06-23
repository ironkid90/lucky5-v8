# Lucky5 v8 Native Desktop Packaging Feasibility & Guide

This document outlines the architecture, feasibility, and implementation steps for packaging the Lucky5 v8 cabinet client as a native desktop executable that connects to the .NET 10 game server backend.

---

## 1. Feasibility & Architecture Options

Packaging the web-native HTML/CSS/JS cabinet into a desktop app is highly feasible and recommended to provide a seamless arcade feel, custom window decorations, hardware integration, and a dedicated, standalone experience.

We evaluate the two primary packaging frameworks:

### Option A: Tauri (Recommended)
Tauri is a framework for building tiny, blazing-fast desktop applications with web frontends.
- **Size**: Extremely lightweight. Binary size is usually **under 10MB** because it utilizes the operating system's native WebView (WebView2 on Windows, WebKit on macOS/Linux) instead of bundling a browser.
- **Resource Usage**: Very low memory and CPU footprint compared to Electron.
- **Backend Integration**: Rust-based backend handles native system APIs, window controls, and can launch the .NET API server as a sidecar process.
- **Security**: Strict security policies by default.

### Option B: Electron
Electron is the industry standard but is resource-heavy.
- **Size**: Large footprint (**120MB+** installer size) because it bundles a full Chromium browser and Node.js runtime.
- **Resource Usage**: High RAM consumption.
- **Ecosystem**: Highly mature with extensive support for hardware and native plug-ins.

---

## 2. Recommended Implementation: Tauri Wrapper

Pointing a Tauri shell to either a remote API host or a locally running .NET backend is simple and clean. Below are the steps to set up Tauri.

### Step 1: Install Prerequisites
1. Install **Rust and Cargo** via [rustup.rs](https://rustup.rs).
2. Install **Node.js** (v18+) for Tauri CLI tooling.
3. Ensure **C++ Build Tools** and the Windows SDK are installed (via Visual Studio Installer).

### Step 2: Initialize Tauri
In the root of the cabinet repository, initialize Tauri:
```bash
npm install @tauri-apps/cli
npx tauri init
```
During initialization, specify:
- **App Title**: `Lucky 5 Video Poker`
- **Window Title**: `Lucky 5`
- **Assets path**: `../server/src/Lucky5.Api/wwwroot` (for local bundle)
- **Dev Server URL**: `http://localhost:5000` (points to the local ASP.NET development backend)

### Step 3: Configure Tauri (`src-tauri/tauri.conf.json`)
Modify the generated configuration file to enable custom window sizing (locked to a 9:16 aspect ratio or scaled portrait viewport matching the cabinet):

```json
{
  "build": {
    "distDir": "../../server/src/Lucky5.Api/wwwroot",
    "devPath": "http://localhost:5000"
  },
  "package": {
    "productName": "lucky5",
    "version": "1.0.0"
  },
  "tauri": {
    "allowlist": {
      "all": false
    },
    "bundle": {
      "active": true,
      "category": "Game",
      "copyright": "",
      "deb": {
        "depends": []
      },
      "externalBin": [],
      "icon": [
        "icons/32x32.png",
        "icons/128x128.png",
        "icons/128x128@2x.png",
        "icons/icon.icns",
        "icons/icon.ico"
      ],
      "identifier": "com.lucky5.cabinet",
      "longDescription": "",
      "macOS": {
        "entitlements": null,
        "exceptionDomain": "",
        "frameworks": [],
        "providerBundleIdentifier": null,
        "signingIdentity": null
      },
      "resources": [],
      "shortDescription": "",
      "targets": "all",
      "windows": {
        "certificateThumbprint": null,
        "digestAlgorithm": "sha256",
        "timestampUrl": ""
      }
    },
    "security": {
      "csp": null
    },
    "windows": [
      {
        "fullscreen": false,
        "height": 1280,
        "resizable": true,
        "title": "Lucky 5",
        "width": 720,
        "minWidth": 360,
        "minHeight": 640
      }
    ]
  }
}
```

### Step 4: Run & Package
Start the desktop application pointing to your locally running server:
```bash
npx tauri dev
```
To compile a standalone production executable (e.g., `.exe` on Windows):
```bash
npx tauri build
```

---

## 3. Bundling the .NET Backend (.NET Sidecar)

For a fully offline native application, you can package the compiled .NET API server directly inside the Tauri bundle as a **Sidecar**.

1. Publish the C# API project as a self-contained single-file executable for the target platform:
   ```bash
   dotnet publish server/src/Lucky5.Api/Lucky5.Api.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o server/dist
   ```
2. Copy the resulting binary (e.g., `Lucky5.Api.exe`) into Tauri's `src-tauri/bin/` folder and suffix it with the target architecture string, e.g., `Lucky5.Api-x86_64-pc-windows-msvc.exe`.
3. Configure `tauri.conf.json` under `externalBin` to recognize the sidecar:
   ```json
   "externalBin": [
     "bin/Lucky5.Api"
   ]
   ```
4. Update Tauri's Rust entrypoint (`src-tauri/src/main.rs`) to spawn the .NET executable when the application starts, and terminate it when the app exits:
   ```rust
   use tauri::api::process::Command;

   fn main() {
     tauri::Builder::default()
       .setup(|app| {
         // Spawn the self-contained .NET 10 API sidecar
         Command::new_sidecar("Lucky5.Api")
           .expect("failed to create sidecar command")
           .spawn()
           .expect("failed to spawn sidecar");
         Ok(())
       })
       .run(tauri::generate_context!())
       .expect("error while running tauri application");
   }
   ```

---

## 4. Verification & Parity Checklist

### Browser Verification
- Open game in standard browser (`http://localhost:5000` after running `./dev.ps1`).
- Ensure layout remains responsive and paytable / Full House jackpot blocks align to the right side underneath the `2 PAIR` row.

### Android / APK Compatibility
- Verify that assets and routes do not use absolute viewport sizes (`vw`/`vh`) that cause scaling collapse.
- Since we used container queries (`cqh` / `cqw`), mobile viewports and APK wrappers automatically render card slots and labels with pixel-perfect aspect ratios.

### Native Desktop Verification
- Run `npx tauri dev` to verify the dedicated window spawns at `720x1280` dimensions.
- Check that window resizing retains aspect ratio letterboxing/centering.
