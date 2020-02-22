using System.Collections.Generic;
using System.Threading.Tasks;

using Pulumi;
using Pulumi.Azure.Core;
using Pulumi.Azure.Storage;
using Pulumi.Azure.AppService;
using Pulumi.Azure.AppService.Inputs;
using Pulumi.Azure.Sql;

class Program
{
    static Task<int> Main()
    {
        return Deployment.RunAsync(() =>
        {

            // Create an Azure Resource Group
            var resourceGroup = new ResourceGroup("resourceGroup", new ResourceGroupArgs
            {
                Name = "pulumi"
            });

            // Create an Azure Storage Account
            var storageAccount = new Account("storage", new AccountArgs
            {
                Name = "wwwcontainer",
                ResourceGroupName = resourceGroup.Name,
                AccountReplicationType = "LRS",
                AccountTier = "Standard",
            });

            var appServicePlan = new Plan("asp", new PlanArgs
            {
                ResourceGroupName = resourceGroup.Name,
                Name = "website",
                Kind = "App",
                Sku = new PlanSkuArgs
                {
                    Tier = "Basic",
                    Size = "B1",
                },
            });

            var container = new Container("zips", new ContainerArgs
            {
                StorageAccountName = storageAccount.Name,
                ContainerAccessType = "private",
            });

            var blob = new ZipBlob("zip", new ZipBlobArgs
            {
                StorageAccountName = storageAccount.Name,
                StorageContainerName = container.Name,
                Type = "block",
                Content = new FileArchive("wwwroot"),
            });

            var codeBlobUrl = SharedAccessSignature.SignedBlobReadUrl(blob, storageAccount);

            var config = new Config();
            var username = config.Get("sqlAdmin") ?? "pulumi";
            var password = config.RequireSecret("sqlPassword");
            var sqlServer = new SqlServer("sql", new SqlServerArgs
            {
                Name = "pulumiserver",
                ResourceGroupName = resourceGroup.Name,
                AdministratorLogin = username,
                AdministratorLoginPassword = password,
                Version = "12.0",
            });

            var database = new Database("db", new DatabaseArgs
            {
                Name = "pulumidatabase",
                ResourceGroupName = resourceGroup.Name,
                ServerName = sqlServer.Name,
                RequestedServiceObjectiveName = "S0",
            });

            var app = new AppService("app", new AppServiceArgs
            {
                Name = "pulumiwebapp",
                ResourceGroupName = resourceGroup.Name,
                AppServicePlanId = appServicePlan.Id,
                AppSettings =
                {
                    { "WEBSITE_RUN_FROM_PACKAGE", codeBlobUrl },
                },
                ConnectionStrings =
                {
                    new AppServiceConnectionStringsArgs
                    {
                        Name = "db",
                        Type = "SQLAzure",
                        Value = Output.Tuple<string, string, string>(sqlServer.Name, database.Name, password).Apply(t =>
                        {
                            (string server, string database, string pwd) = t;
                            return $"Server= tcp:{server}.database.windows.net;initial catalog={database};userID={username};password={pwd};Min Pool Size=0;Max Pool Size=30;Persist Security Info=true;";
                        }),
                    },
                },
            });

            return new Dictionary<string, object?>
            {
                { "endpoint", app.DefaultSiteHostname },
                { "storageConnectionString", storageAccount.PrimaryConnectionString },
            };

        });
    }
}
