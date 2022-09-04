﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using CoreHelpers.WindowsAzure.Storage.Table.Backup.Abstractions;
using Microsoft.Extensions.Logging;

namespace CoreHelpers.WindowsAzure.Storage.Table.Backup
{
    public class BackupContext : IBackupContext
    {
        private ILogger<BackupContext> _logger;
       
        private string _targetConnectionString;
        private string _targetContainer;
        private string _targetPath;
        private string _targetTableNamePrefix;
        private BlobServiceClient _blobServiceClient;
       
        public BackupContext(ILogger<BackupContext> logger, string connectionString, string container, string path, string tableNamePrefix)
        {
            _logger = logger;
            _targetConnectionString = connectionString;
            _targetContainer = container;
            _targetPath = path;
            _targetTableNamePrefix = tableNamePrefix;

            _blobServiceClient = new BlobServiceClient(_targetConnectionString);
        }

        public async Task Backup(IStorageContext storageContext, string[] excludedTables = null, bool compress = true)
        {
            using (_logger.BeginScope("Starting backup procedure..."))
            {

                // generate the excludeTables
                var excludedTablesList = new List<string>();
                if (excludedTables != null)
                {
                    foreach (var tbl in excludedTables)
                        excludedTablesList.Add(tbl.ToLower());
                }

                // get all tables 
                var tables = await storageContext.QueryTableList();
                _logger.LogInformation($"Processing {tables.Count} tables");

                // prepare the backup container
                _logger.LogInformation($"Creating target container {_targetContainer} if needed");
                var blobContainerClient = _blobServiceClient.GetBlobContainerClient(_targetContainer);
                if (!await blobContainerClient.ExistsAsync())
                    await blobContainerClient.CreateIfNotExistsAsync();

                // prepare the memory stats file
                var memoryStatsFile = $"{Path.GetTempFileName()}.csv";
                using (var statsFile = new StreamWriter(memoryStatsFile))
                {
                    // use the statfile
                    _logger.LogInformation($"Statsfile is under {memoryStatsFile}...");
                    statsFile.WriteLine($"TableName,PageCounter,ItemCount,MemoryFootprint");

                    // visit every table
                    foreach (var tableName in tables)
                    {

                        // filter the table prefix
                        if (!String.IsNullOrEmpty(_targetTableNamePrefix) && !tableName.StartsWith(_targetTableNamePrefix, StringComparison.CurrentCulture))
                        {
                            _logger.LogInformation($"Ignoring table {tableName}...");
                            continue;
                        }

                        // check the  excluded tables
                        if (excludedTablesList.Contains(tableName.ToLower()))
                        {
                            _logger.LogInformation($"Ignoring table {tableName} (is part of excluded tables)...");
                            continue;
                        }

                        using (_logger.BeginScope($"Processing backup for table {tableName}..."))
                        {                            
                            // do the backup
                            var fileName = $"{tableName}.json";
                            if (!string.IsNullOrEmpty(_targetPath)) { fileName = $"{_targetPath}/{fileName}"; }
                            if (compress) { fileName += ".gz"; }

                            // open block blog reference
                            var blobClient = blobContainerClient.GetBlobClient(fileName);

                            // open the file stream 
                            if (compress)
                                _logger.LogInformation($"Writing backup to compressed file");
                            else
                                _logger.LogInformation($"Writing backup to non compressed file");

                            // do it                    
                            using (var backupFileStream = await blobClient.OpenWriteAsync(false))
                            {
                                using (var contentWriter = new ZippedStreamWriter(backupFileStream, compress))
                                {
                                    var pageCounter = 0;
                                    var itemCounter = 0;

                                    var pageLogScope = default(IDisposable);

                                    await storageContext.ExportToJsonAsync(tableName, contentWriter, (ImportExportOperation operation) =>
                                    {
                                        switch (operation)
                                        {
                                            case ImportExportOperation.processingPage:
                                                {
                                                    pageCounter++;
                                                    pageLogScope = _logger.BeginScope($"Processing page #{pageCounter}");
                                                    break;
                                                }
                                            case ImportExportOperation.processingItem:
                                                {
                                                    itemCounter++;
                                                    break;
                                                }
                                            case ImportExportOperation.processedPage:
                                                {
                                                    _logger.LogInformation($"#{itemCounter} processed!");
                                                    statsFile.WriteLine($"{tableName},{pageCounter},{itemCounter},{Process.GetCurrentProcess().WorkingSet64}");
                                                    pageLogScope.Dispose();
                                                    pageLogScope = null;
                                                    break;
                                                }                                                
                                        }                                     
                                    });
                                }
                            }

                            // ensure we clean up the memory beause sometimes 
                            // we have to much referenced data
                            GC.Collect();

                            // flush the statfile 
                            await statsFile.FlushAsync();
                        }
                    }
                }
            }
        }
        
        public void Dispose()
        {
                       
        }
    }
}

