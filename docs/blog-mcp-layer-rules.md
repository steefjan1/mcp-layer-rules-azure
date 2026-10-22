---
title: "Access Is Not Authority: Four Rules for the MCP Layer on Azure Functions"
slug: mcp-layer-rules-azure-functions
focus_keyphrase: "MCP layer Azure Functions"
meta_description: "A LinkedIn infographic gave four rules for the MCP layer on top of your API. I built them on Azure Functions, added what the comments asked for, and measured what the extension actually gives you."
categories: [Azure, AI, Integration]
tags: [mcp, azure functions, entra id, agents, api]
cover: images/mcp-layer-four-rules.png
---

"You don't pick MCP or an API. You stack them." That line opened a LinkedIn post last week, with a tidy picture: AI agent → MCP server → your existing API, and four rules for the MCP layer. Keep auth in the API. Build tools around tasks. Expose less. Label reads and writes.

I agreed with all four, and I had already built most of them in my [MCP vs API series](https://dev.to/steefjan_wiggers_34a415b/mcp-vs-api-is-the-wrong-question-392f). What made me open an editor again was the comment thread. One reader asked what a single agent gains from MCP when the API already does the job. Another said that before authority there is provenance: is the offer the agent is about to book even genuine? A third added that access is not authority, and that a successful tool call is not a successful business outcome.

So this post does three things. It puts the four rules on Azure Functions. It adds the three concerns from the comments. And it measures the one rule the Azure Functions MCP extension cannot keep for you.

The sample is at [github.com/steefjan1/mcp-layer-rules-azure](https://github.com/steefjan1/mcp-layer-rules-azure).

![MCP vs API on Azure: the MCP server is one more Functions app calling the API you already have](images/mcp-vs-api-azure.png)

### The stack, on Azure

Two Function apps, one storage account, one Entra app registration.

The API (`func-stayapi`) is an HTTP-triggered .NET 8 app behind Functions built-in authentication. It owns everything that decides something: listings, quotes, bookings, cancellations, roles, idempotency, audit. The MCP server (`func-staymcp`) is a second Functions app with the MCP tool trigger. It owns tool names, descriptions, a confirmation flag, and one signature check. It has no business rules, and it has no table it writes to.

That split answers the first commenter. With one agent and one API, the MCP server is not improving the API. It is the door the model reads, and it is where you decide what the model may even see. The API stays the door software calls. Same kitchen, two doors, which is the whole point of the series this post belongs to.

### Rule 1: the MCP server gets its own token

The infographic says the spec forbids passing the user's token through. That is right: the MCP authorization spec is explicit that a server must not forward the token it received from the client to an upstream API. On Azure the natural way to obey is a managed identity.

The MCP app has a user-assigned identity. On every API call it asks `DefaultAzureCredential` for a token with the API's app ID URI as the scope. The API app has built-in authentication switched on in Bicep, with the app registration as the audience, so the platform validates issuer, audience, signature and expiry before the function runs. The function reads roles from `X-MS-CLIENT-PRINCIPAL` and nothing else.

There is a detail the Functions extension makes easy. The MCP tool trigger hands your function a `ToolInvocationContext` with no caller identity at all. There is no user token in scope to forward by accident. Rule 1 is kept partly because the trigger does not give you the rope.

![MCP Inspector calling find_stays on the deployed server: the listings come back, so the MCP app's own token passed built-in authentication and the Stays.Read role check](images/find-stays.png)

### Rule 2 and rule 3 pull in opposite directions

The API has six routes. The MCP server has four tools: `find_stays`, `quote_stay`, `book_stay`, `cancel_stay`. `GET /bookings/{id}` and the public key endpoint are not tools. The agent gets the booking back from `book_stay`, and the key is something the MCP server fetches, not the model.

Then I measured, because rule 3 is a number. `measure/list_tools.py` runs `tools/list` against the server and counts the tokens of each definition the client will put in front of the model. The four task-shaped tools cost 589 tokens in this sample (cl100k_base, live `tools/list` from the deployed server). The same six routes as six endpoint-shaped tools with one-line descriptions cost 384.

Fewer tools did not mean fewer tokens. The task shape spends its tokens on when-to-call prose: call `quote_stay` before `book_stay`, pass the signed quote unchanged, get a yes before `confirm=true`. The endpoint shape leaves that sequence, and the idempotency key, to the model. I will take 205 tokens over a model that invents its own booking protocol, but it is worth knowing which rule you are paying for. The script fails CI over a budget, so the surface cannot grow quietly.

### Rule 4: the label is not on the wire

This is the finding. The infographic says to label reads and writes with `readOnlyHint` and `destructiveHint`, and it is careful to add that they are hints, so keep a confirmation on anything that pays or deletes.

The Azure Functions MCP trigger attribute takes a tool name and a description. That is it. There is no parameter for annotations, and the measurement script confirms what `tools/list` returns: no `annotations` object on any of the four tools. A client that uses annotations to decide what runs without asking sees four unlabelled tools and has to assume the worst, which in fairness is what the MCP spec tells clients to do.

So the sample puts the label where this extension lets it live. The description starts with "Read-only", "Writes" or "Destructive". The two writing tools take a `confirm` argument and refuse with prose when it is not true. The host.json `instructions` say the same thing once for the whole server. None of that is a security control, and the comment thread is right that annotations are not either. The control is the next section.

![book_stay called without confirm: the tool returns prose telling the model to show the total and dates and ask for a yes, and nothing is written](images/book_stay_not_confirmed.png)

![Four rules for the MCP layer, and where each one is enforced](images/mcp-layer-four-rules.png)

### What the comments added: authority, provenance, recovery

**Authority.** Three app roles on the API registration: `Stays.Read`, `Bookings.Create`, `Bookings.Cancel`. `grant-roles.ps1` assigns them to the MCP identity, and that script is the only place where "what may the agent change" is decided. Run it with `Stays.Read` only and `book_stay` becomes a tool the agent can reach and the API refuses, every call, with a 403 the MCP server renders as prose: this action is not available through the assistant. Access is not authority. The REST door keeps working for a caller that holds the role.

**Provenance.** The booking commenter's point was that nothing in MCP or the API proves the offer the agent acts on was issued by the host and not changed on the way. The sample's API signs every quote with ECDSA P-256 over a canonical body (listing, dates, guests, total, expiry) and publishes the public key at `/.well-known/stay-keys`. The MCP server verifies the signature before it calls `book_stay`, fails closed, and caches the key for five minutes, the same revocation window the commenter's protocol uses. The API verifies again on `POST /bookings`. Change the total between `quote_stay` and `book_stay`, whether the model did it or a prompt injection did, and the write call is never made.

![quote_stay result in MCP Inspector: the quote body, the key id and the ECDSA signature over it; this exact object is what book_stay must receive unchanged](images/quote-stays.png)

**Recovery.** `POST /bookings` requires an `Idempotency-Key`. The MCP server derives it from the quote id, so a retry of the same quote is the same booking. The API claims the key in Table storage before it writes, returns the existing booking with `Idempotent-Replayed: true` on a repeat, and loses a race cleanly. An audit table gets a row per stage: requested, denied or rejected, authorized, attempted, completed, replayed. That is the "what was requested, approved, attempted, and actually completed" record the comment asked for, in the kitchen, keyed by quote.

### What the first run against Azure taught me

Two things I did not plan for, both worth knowing before you build the same.

The MCP extension parses arguments before it binds them. I declared `checkIn` as a `string` and the model sent `"2026-12-05"`. The function received `12/05/2026 00:00:00 +00:00`, the `ToString()` of a `DateTimeOffset` the extension had helpfully made on the way in. The API, which demands `yyyy-MM-dd`, refused it. The fix is small (the tool normalises the date before it calls the API) but the lesson is bigger: the door is allowed to be forgiving, the kitchen is not, and you only find out where the extension is "helping" by running it.

![The booking returned by book_stay with confirm=true: status confirmed, createdBy is the MCP app's managed identity object id, not a user](images/book_stay_confirmed.png)

The audit row names the agent, not the user. `createdBy` on a booking made through the MCP door is the object id of the MCP app's managed identity, because that is who called the API. The user who said yes in VS Code is nowhere in the chain. That is rule 1 working exactly as written, and it is also a gap: if you need "which human approved this" in the record, the MCP server has to carry it as data (a claim from its own authenticated client, written into the request body) because the API will never see a user token. I covered what it takes to authenticate that client in the 401 post.

### Where this is the wrong answer

If one deterministic system calls your API, add nothing. MCP earns its place when a model chooses the call, or when more than one agent needs the same tools with the same descriptions.

If your clients are end-user assistants that expect OAuth, the Functions system key on the MCP endpoint is the wrong door lock. Switch on built-in MCP authentication on the MCP app as well; I walked through that, and the invalid_target wall behind it, in [What a 401 Means to an MCP Client](https://dev.to/steefjan_wiggers_34a415b/what-a-401-means-to-an-mcp-client-1a5i).

If you need annotations on the wire today, this extension version does not give them to you. API Management's MCP export and the MCP C# SDK hosted on your own container both can. That trade is a post of its own.

And a signed quote proves the price is the one the host issued. It does not prove the host is honest. Provenance answers "is this real", not "is this a good deal".

### What to take away

The infographic's four rules hold on Azure, and three of them are cheap: a managed identity, a tool-per-task layout, and a script that counts tokens. The fourth, labelling, is where the platform leaves a gap, and the comment thread had already told me what fills it. Not a hint. A role on the API, a signature on the thing being acted on, and an idempotency key on the write.

The MCP layer decides what the model sees. The API decides what happens. Keep it that way and the diagram stays small.

**Sources**

- [MCP specification: Authorization](https://modelcontextprotocol.io/specification/2025-06-18/basic/authorization) (token passthrough prohibition)
- [MCP specification: Tools, annotations](https://modelcontextprotocol.io/specification/2025-06-18/server/tools)
- [Anthropic, Writing tools for agents](https://www.anthropic.com/engineering/writing-tools-for-agents)
- [Azure Functions MCP bindings](https://learn.microsoft.com/azure/azure-functions/functions-bindings-mcp) and [MCP tool trigger](https://learn.microsoft.com/azure/azure-functions/functions-bindings-mcp-tool-trigger)
- [Microsoft.Azure.Functions.Worker.Extensions.Mcp 1.6.0 on NuGet](https://www.nuget.org/packages/Microsoft.Azure.Functions.Worker.Extensions.Mcp/1.6.0)
- [Azure App Service and Functions built-in authentication](https://learn.microsoft.com/azure/app-service/overview-authentication-authorization)
- Earlier posts in the series: [MCP vs API Is the Wrong Question](https://dev.to/steefjan_wiggers_34a415b/mcp-vs-api-is-the-wrong-question-392f), [What the Agent Pays for Discovery](https://dev.to/steefjan_wiggers_34a415b/what-the-agent-pays-for-discovery-221a), [Tool Descriptions Are the Contract](https://dev.to/steefjan_wiggers_34a415b/tool-descriptions-are-the-contract-5488)

---

<!-- PRE-PUBLISH CHECKLIST (not part of the post)
1. DONE 2026-10-22: dotnet build 0 warnings 0 errors; azd up to rg-mcp-layer-rules (swedencentral); grant-roles Stays.Read + Bookings.Create; mcp_smoke.py 7/7 PASS against Azure.
2. DONE 2026-10-22: list_tools.py against the deployed server returned no annotations on any tool (server stay-mcp 1.0.0, protocol 2025-06-18). Rule 4 section stands.
3. DONE 2026-10-22: tiktoken numbers in the post (589 task-shaped live and offline, 384 endpoint-shaped, 205 difference).
4. DONE 2026-10-22: four Inspector screenshots placed (find-stays, quote-stays, book_stay_not_confirmed, book_stay_confirmed). Optional extras not taken: tampered-quote refusal, audit table (needs Storage Table Data Reader on the account), Copilot asking for a yes (this VS Code build would not expose the MCP tools to the chat; Inspector used instead).
5. The MCP spec section URLs point at the 2025-06-18 revision; check whether the site now redirects to a newer revision and update.
6. DONE: github.com/steefjan1/mcp-layer-rules-azure exists; push pending.
-->
