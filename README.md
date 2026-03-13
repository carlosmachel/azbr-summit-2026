# azbr-summit-2026

Demo used in the AzureBrasil Summit talk held on March 14, 2026.

## Description
This project is a backend API developed in .NET 10, demonstrating an automated decision workflow with AI agents for credit validation, KYC, fraud, and income. It was presented as a practical example at AzureBrasil Summit 2026.

## How to run the project locally
1. **Prerequisites:**
   - [.NET 10 SDK](https://dotnet.microsoft.com/download)
   - Azure OpenAI account and resources (or adjust settings for your AI provider)
2. **Clone the repository:**
   ```bash
   git clone <repository-url>
   cd azbr-summit-2026
   ```
3. **Configure environment variables:**
   - Edit `src/WorkflowApi/appsettings.Development.json` with your Azure OpenAI credentials:
     - `AZURE_OPENAI_ENDPOINT`
     - `AZURE_OPENAI_DEPLOYMENT_NAME`
     - `TENANT_ID`, `CLIENT_ID`, `CLIENT_SECRET` *(optional if you are already authenticated via `az login`)*
4. **Run the application:**
   ```bash
   cd src/WorkflowApi
   dotnet run
   ```
   The API will be available at `http://localhost:5204`.
   
   To open the graphical interface, access [http://localhost:5204/devui](http://localhost:5204/devui) in your browser.

5. **Default test input:**
   To test the workflow, use the following credit application JSON as input:
   ```json
   {
     "amount": 50000,
     "currency": "BRL",
     "cpf": "123.456.789-00"
   }
   ```

## Directory structure
```
azbr-summit-2026/
├── src/
│   ├── WorkflowApi/           # Main API project
│   │   ├── Executors/         # Workflow orchestration agents
│   │   ├── Frauds/            # Fraud agents and tools
│   │   ├── Incomes/           # Income agents and tools
│   │   ├── Kycs/              # KYC agents and tools
│   │   ├── Properties/        # Environment settings
│   │   ├── Program.cs         # Application entry point
│   │   └── ...
│   └── WorkflowApi.Tests/     # Test project (empty)
├── LICENSE
├── README.md
└── azbr-summit-2026.sln
```

## Workflow architecture
The credit workflow uses a **fan-out / fan-in** pattern orchestrated by custom executors:

1. **`ConcurrentStartAgent`** — Entry point that receives the credit application and fans out the request to all validation agents in parallel.
2. **KYC, Fraud, Income agents** — Three AI agents run concurrently, each performing its own validation (identity, fraud risk, income capacity).
3. **`ConcurrentAggregationExecutor`** — Barrier executor that collects the responses from all three agents. Once all results arrive, it parses them and produces a final **`DecisionResult`** (Approved, Rejected, or Review) with conditions and a summary.

```
                         ┌──────────┐
                         │  Start   │
                         │  Agent   │
                         └────┬─────┘
                   ┌──────────┼──────────┐
                   ▼          ▼          ▼
              ┌────────┐ ┌────────┐ ┌────────┐
              │  KYC   │ │ Fraud  │ │ Income │
              └───┬────┘ └───┬────┘ └───┬────┘
                  └──────────┼──────────┘
                         ┌───▼───┐
                         │ Aggr. │
                         │Executor│
                         └───┬───┘
                             ▼
                      DecisionResult
```

## Technologies used
- .NET 10 (C#)
- Azure OpenAI
- Microsoft.Agents.AI and Workflows
- Azure Identity
- ASP.NET Core

## How to contribute
1. Fork the project
2. Create a branch for your feature or fix: `git checkout -b my-feature`
3. Commit your changes: `git commit -m 'My contribution'`
4. Push to your fork: `git push origin my-feature`
5. Open a Pull Request

## License
This project is licensed under the MIT license. See the [LICENSE](LICENSE) file for more details.
