
# CSharp Secure Chat

CSharp Secure Chat is a small but production‑minded real‑time chat application built with **ASP.NET Core + SignalR** on the backend and **Angular** on the frontend.

The goal of the project is to demonstrate:

- Robust anti‑spam protections (per‑IP rate limiting for different actions).
- Temporary **banning** of abusive clients.
- Safe handling of **usernames** and connection lifecycle.
- Strong **XSS protection** on the backend, even if the frontend is implemented in an unsafe way.
- Clean separation of layers (`Domain`, `Application`, `Infrastructure`, `Web`) and testable server logic.

---

## High‑Level Architecture

### Backend

- **Technology:** ASP.NET Core, SignalR.
- **Project:** `Server/Server`
- **Entry point:** `Program.cs`
- **Main hub:** `ChatHub` (in `Server/Web/Hubs`)

Responsibilities of the backend:

- Manage SignalR connections and broadcast messages.
- Track usernames per active connection.
- Store and fetch chat history via `IMessageStore`.
- Enforce rate limits and bans via `IRateLimitService` and `IBanService`.
- Sanitize user input using `TextHelper.SanitizePlainText` to prevent XSS.

### Frontend

- **Technology:** Angular
- **Project root:** `Client`
- Connects to the SignalR hub (`/chathub`).
- Renders:
  - connection status
  - username input
  - messages list
  - notification banners (ban, connection errors)
- By design, the frontend **can be written in an unsafe way** (e.g., using `[innerHTML]`), and the backend is still responsible for ensuring that no XSS is possible.

### Tests

- **Project:** `Server/Tests`
- Contains unit and/or integration tests for server logic (rate limiting, banning, sanitization, etc.).

---

## Chat Functionality

### Messages

- A connected client can:
  - set a username,
  - send text messages,
  - load the latest N messages via `GetHistory`.

- Messages are represented by a `ChatMessage` entity (in `Domain`):
  - `Id`
  - `ConnectionId`
  - `UserName`
  - `Text`
  - `Timestamp`

- Storage is abstracted behind `IMessageStore` (in `Application` / `Infrastructure`).

### Usernames in Memory

Usernames are **not** persisted; they live in memory and are tied to **connection IDs**.

Two dictionaries are used:

- `ConcurrentDictionary<string, string> _userNames`  
  Maps `connectionId -> userName`.

- `ConcurrentDictionary<string, string> _nameOwners`  
  Maps `userName -> connectionId` (case‑insensitive), making usernames unique.

Lifecycle:

1. When a client connects, it gets a SignalR `connectionId` with no username.
2. When the client calls `ChangeName(newName)`:
   - The name is sanitized using `TextHelper.SanitizePlainText`.
   - If the sanitized name is empty, the change is ignored.
   - If another connection already owns that name, a `HubException` is thrown.
   - Otherwise, the name is reserved and stored in both dictionaries.
   - The username is updated on all previous messages from that connection via `_messageStore.UpdateUserNameAsync(connectionId, newName)`.
3. When the connection is closed (normal disconnect **or** ban), the hub:
   - removes the entry from `_userNames`,
   - frees the username from `_nameOwners`, making it available again.

> As a result, disconnecting always means **losing the username**. On reconnect, the user must pick a name again (if it is still available).

---

## Anti‑Spam and Bans

### IP Tracking

The hub tracks:

- A mapping `connectionId -> ip` (`_connectionIps`).
- A per‑IP counter of active connections (`_ipConnectionCounts`).

Each time a connection is opened or closed, these structures are updated.

### Rate Limiting

The service `IRateLimitService` enforces per‑IP limits for several actions:

- connect (`ChatAction.Connect`)
- change name (`ChatAction.ChangeName`)
- send message (`ChatAction.SendMessage`)
- get history (`ChatAction.GetHistory`)

If the rate limit for a specific IP and action is exceeded, the hub treats it as **spam**.

### Banning

When spam is detected, or when an IP opens too many concurrent connections, the hub:

1. Uses `IBanService.Ban(ip, duration)` to mark the IP as banned for a configured time (e.g. 10 seconds), taken from `SpamProtectionOptions.BanDurationSeconds`.
2. Sends a `Banned` event to the offending client:

   ```json
   {
     "message": "You are temporarily blocked due to spam. Please try again later.",
     "retryAfterSeconds": 10
   }
   ```

3. Immediately disconnects the client by calling `Context.Abort()`.

While an IP is banned:

- **Every hub method** first calls `HandleIfBannedAsync(ip, contextInfo)`.
- If the IP is still banned, the method:
  - sends a `Banned` payload to the caller,
  - aborts the connection,
  - returns without performing any action.

#### Why we disconnect on ban

Banning without closing the WebSocket would allow the client to:

- keep the existing `connectionId`,
- keep its username,
- possibly keep sending messages, if some paths forget to check the ban.

To make the system safe and predictable:

- A banned IP is **always** forced to reconnect after the ban expires.
- The old `connectionId` and username are dropped.
- On a new connection, the user must set a name again (if it is still free).

This cleanly separates the life of an IP, a connection, and a username.

---

## XSS Protection

All user‑provided text (usernames and message text) is sanitized on the backend via:

```csharp
TextHelper.SanitizePlainText(input, maxLength, allowNewLines)
```

Sanitization steps:

1. Normalize Unicode (`NormalizationForm.FormC`).
2. Remove control characters (except `\r` / `\n` if new lines are allowed).
3. Trim to a maximum length.
4. HTML‑escape critical characters:
   - `<` → `&lt;`
   - `>` → `&gt;`
   - `&` → `&amp;`

This ensures that:

- Any HTML or JavaScript sent by the client is turned into plain text.
- Even if the frontend renders messages via `[innerHTML]`, the browser only sees text like
  `&lt;script&gt;alert('XSS')&lt;/script&gt;` instead of a real `<script>` tag.

The backend assumes **zero trust** to the frontend and is designed to remain safe even with a deliberately vulnerable UI.

---

## Requirements

To build and run the project locally you need:

- **.NET SDK**:  
  - .NET 8.0 or later (the solution is structured as a modern ASP.NET Core app).
- **Node.js**:  
  - Node.js 18.x or 20.x (LTS recommended).
- **Angular CLI**:  
  - Installed globally (version compatible with `Client/package.json`, for example `npm install -g @angular/cli`).
- **npm** or **yarn**:  
  - npm comes with Node.js; use the same package manager as in the project.

Development was primarily tested on:

- Windows 10/11 + Visual Studio / VS Code  
- but Linux/macOS should work as well with the same toolchain.

---

## Installation & Local Setup

Below are two parts:

1. **Initial installation** (cloning the repo and installing dependencies).
2. **Running the project locally** (backend + frontend).

### 1. Initial Installation

1. **Clone the repository**

   ```bash
   git clone https://github.com/<your-account>/CSharp-Secure-Chat.git
   cd CSharp-Secure-Chat
   ```

2. **Backend dependencies**

   Go to the server project:

   ```bash
   cd Server/Server
   dotnet restore
   ```

   This restores all NuGet packages for the ASP.NET Core project.

3. **Frontend dependencies**

   In a separate terminal (or after going back), install client dependencies:

   ```bash
   cd Client
   npm install
   ```

   This installs all npm packages required for the Angular app.

> At this point, the project is “installed”: all dependencies for both backend and frontend are downloaded.

---

### 2. Running the Project Locally

You usually run **backend and frontend in parallel**.

#### 2.1 Start the Backend (ASP.NET Core + SignalR)

From the root of the repo:

```bash
cd Server/Server
dotnet run
```

By default the app will listen on URLs specified in `appsettings.json` / `launchSettings.json` (usually something like):

- `https://localhost:5001`
- `http://localhost:5000`

Make sure the Angular client is configured to use the same URL for the SignalR hub, e.g.:

```ts
// environment.ts / environment.prod.ts
export const environment = {
  production: false,
  hubUrl: 'https://localhost:5001/chathub'
};
```

(or `http://localhost:5000/chathub`, depending on your configuration).

#### 2.2 Start the Frontend (Angular)

Open a second terminal:

```bash
cd CSharp-Secure-Chat/Client
npm start
# or, depending on your scripts:
# npm run start
# or:
# ng serve
```

By default Angular dev server runs on:

```text
http://localhost:4200
```

Open that URL in your browser.

If backend URLs are configured correctly in the environment file, the client will:

- connect to the SignalR hub,
- show “Connected” status,
- let you set your username and start chatting.

---

## Running Tests

Server tests live under `Server/Tests`.

To run all server tests:

```bash
cd CSharp-Secure-Chat/Server
dotnet test
```

This will run unit / integration tests for the backend logic.

---

## Notes

- The project is designed to be safe even with an intentionally unsafe frontend (e.g. using `[innerHTML]` for rendering messages), thanks to backend‑side sanitization.
- Anti‑spam and rate limiting are configurable via `SpamProtectionOptions` (e.g. ban duration, max connections per IP).
- Usernames are ephemeral and tied to the lifetime of a SignalR connection. Disconnecting (including forced disconnect due to a ban) frees the name for reuse.

