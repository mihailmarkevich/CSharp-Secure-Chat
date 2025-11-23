# CSharp Secure Chat

CSharp Secure Chat is a small but production‑minded real‑time chat application built with **ASP.NET Core + SignalR** on the backend and **Angular** on the frontend.

The goal of the project is to demonstrate:

- Strong anti‑spam protections (per‑IP rate limiting per action).
- Temporary **banning** of abusive clients.
- Safe handling of usernames and the connection lifecycle.
- Robust **XSS protection** enforced entirely by the backend.
- Clean architecture with clear separation of layers.

---

## High‑Level Architecture

### Backend

- **Tech:** ASP.NET Core 8 + SignalR  
- **Path:** `Server/Server`  
- **Entry point:** `Program.cs`  
- **Hub:** `ChatHub`  

Backend responsibilities:

- Manage SignalR client connections.
- Enforce username uniqueness.
- Handle chat message storage via `IMessageStore`.
- Apply rate‑limiting + spam detection via `IConnectionRegistry`.
- Use `IBanService` to ban abusive clients.
- Guarantee XSS‑safe rendering through enforced sanitization.

### Frontend

- **Tech:** Angular  
- **Path:** `Client/`
- Connects to `/chathub`
- Renders:
  - connection status
  - username controls
  - messages with timestamps
  - banners for ban + connection errors

Frontend intentionally uses `[innerHTML]` → backend ensures all sanitization.

### Tests

There are **no test projects** in this repository.  
Tests were intentionally removed to simplify review and installation for external users.

---

## Chat Functionality

### Message Model

```
Id (Guid)
ConnectionId
UserName
Text
Timestamp
```

Messages are persisted via `IMessageStore`.

### Username Lifecycle

Usernames are in‑memory only and linked to the **connectionId**.

Rules:

1. New connections start without a username.
2. On `ChangeName(newName)`:
   - name is sanitized
   - empty/invalid → ignored
   - already taken → backend throws `HubException`
   - otherwise name is assigned and broadcasted
3. Disconnect → username is freed for reuse.

This ensures consistent behavior for reconnects and bans.

---

## Anti‑Spam / Rate Limiting / Banning

### IP Tracking

`IConnectionRegistry` maintains:

- active connections per IP  
- mapping of `connectionId → ip`

Too many connections → connection immediately gets a **ban payload** and is aborted.

### Rate Limiting

`IRateLimitService` monitors frequency of:

- connect  
- change name  
- send message  
- get history  

Exceeding limits = spam.

### Ban Behavior

When spam is detected:

1. `IBanService.Ban(ip)` bans the IP temporarily.
2. Hub sends:
   ```json
   { "message": "You are temporarily blocked due to spam." }
   ```
3. WebSocket is terminated via `Context.Abort()`.

All hub methods check ban state before executing.

### Why Disconnect?

Disconnecting ensures:

- banned clients cannot reuse the old `connectionId`
- user loses username → prevents ghost users
- prevents message injection during ban

---

## XSS Protection

All input passes through:

```csharp
TextHelper.SanitizePlainText(input, maxLength, allowNewLines)
```

This:

- normalizes Unicode
- strips unsafe control characters
- enforces max length
- escapes HTML (`<`, `>`, `&`)

Result:

Even if frontend uses `[innerHTML]`, the browser will display **escaped text**, never executable HTML/JS.

Backend assumes **zero trust** toward the frontend.

---

## Requirements

### Backend

- .NET SDK **8.0+**

### Frontend

- Node.js **18.x or 20.x**
- Angular CLI:
  ```bash
  npm install -g @angular/cli
  ```

Works on:

- Windows
- Linux
- macOS

---

## Installation

### 1. Clone the repository

```bash
git clone https://github.com/<your-account>/CSharp-Secure-Chat.git
cd CSharp-Secure-Chat
```

### 2. Install backend dependencies

```bash
cd Server/Server
dotnet restore
```

### 3. Install frontend dependencies

```bash
cd Client
npm install
```

---

## Running the Project Locally

### Start backend

```bash
cd Server/Server
dotnet run
```

Backend default URLs:

- http://localhost:5000  
- https://localhost:5001

### Configure frontend environment

File: `Client/src/environments/environment.ts`

```ts
export const environment = {
  production: false,
  chatHubUrl: "https://localhost:5001/chathub"
};
```

(or http://localhost:5000/chathub)

### Start frontend

```bash
cd Client
npm start
```

Open:

```
http://localhost:4200
```

You can now chat, set names, and observe automatic banning logic.

---

## Notes

- Designed to be secure even with an unsafe UI.
- Usernames are released on disconnect/bans.
- All input is sanitized server‑side.
- Bans enforce reconnection and clean lifecycle.

