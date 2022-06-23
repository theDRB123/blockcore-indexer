using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Blockcore.Indexer.Cirrus.Storage;
using Blockcore.Indexer.Cirrus.Storage.Mongo;
using Blockcore.Indexer.Cirrus.Storage.Mongo.SmartContracts;
using Blockcore.Indexer.Cirrus.Storage.Mongo.Types;
using Blockcore.Indexer.Core.Settings;
using Blockcore.Indexer.Core.Sync.SyncTasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Blockcore.Indexer.Cirrus.Sync.SyncTasks;

public class SmartContractSyncRunner : TaskRunner
{
   ICirrusStorage storage;
   ICirrusMongoDb db;
   ILogger<SmartContractSyncRunner> logger;

   readonly Dictionary<string, Func<string,Task>> supportedSmartContractTypes;

   public SmartContractSyncRunner(IOptions<IndexerSettings> configuration,
      ICirrusStorage storage,
      IComputeSmartContractService<DaoContractTable> daoContractService,
      IComputeSmartContractService<StandardTokenContractTable> standardTokenService,
      IComputeSmartContractService<NonFungibleTokenContractTable> nonFungibleTokenService,
      ICirrusMongoDb db,
      ILogger<SmartContractSyncRunner> logger)
      : base(configuration, logger)
   {
      this.storage = storage;
      this.db = db;
      this.logger = logger;

      supportedSmartContractTypes = new Dictionary<string, Func<string,Task>>
      {
         { "DAOContract", daoContractService.ComputeSmartContractForAddressAsync },
         { "StandardToken", standardTokenService.ComputeSmartContractForAddressAsync },
         { "NonFungibleToken", nonFungibleTokenService.ComputeSmartContractForAddressAsync },
      };
   }

   public override async Task<bool> OnExecute()
   {
      var smartContractAddresses = await storage.GetSmartContractsThatNeedsUpdatingAsync(supportedSmartContractTypes.Keys.ToArray());

      var types = db.CirrusContractCodeTable.AsQueryable()
         .Where(_ => smartContractAddresses.Contains(_.ContractAddress))
         .Select(_ => new { address = _.ContractAddress, contractType = _.CodeType })
         .ToArray();

      foreach (var type in types)
      {
         try
         {
            if (!supportedSmartContractTypes.ContainsKey(type.contractType))
               continue;

            Func<string, Task> serviceToRun = supportedSmartContractTypes[type.contractType];

            await serviceToRun.Invoke(type.address);
         }
         catch (Exception e)
         {
            //We won't stop the loop so all other smart contracts can be computed
            logger.LogError(type.address,e);
         }
      }

      return true;
   }
}
