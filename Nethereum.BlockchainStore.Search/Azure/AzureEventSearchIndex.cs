﻿using Microsoft.Azure.Search;
using Microsoft.Azure.Search.Models;
using Microsoft.Rest.TransientFaultHandling;
using Nethereum.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Nethereum.BlockchainStore.Search.Azure
{
    public class AzureEventSearchSearchIndex<TEvent> : IAzureEventSearchIndex<TEvent> where TEvent : class
    {
        private readonly EventSearchIndexDefinition<TEvent> _eventSearchDefinition;
        private readonly Index _index;
        private readonly ISearchIndexClient _indexClient;

        public int Indexed { get; private set; }

        public AzureEventSearchSearchIndex(EventSearchIndexDefinition<TEvent> eventSearchDefinition, Index index, ISearchIndexClient indexClient)
        {
            _eventSearchDefinition = eventSearchDefinition;
            _index = index;
            _indexClient = indexClient;
        }

        public async Task IndexAsync(EventLog<TEvent> log)
        {
            await IndexAsync(new[] {log});
        }

        public async Task IndexAsync(IEnumerable<EventLog<TEvent>> logs)
        {
            var documents = logs.Select(l => l.ToAzureDocument(_eventSearchDefinition)).ToArray();
            await BatchUpdateAsync(documents);
            Indexed += documents.Length;
        }

        public async Task<DocumentSuggestResult<Dictionary<string, object>>> SuggestAsync(string searchText, bool fuzzy = true)
        {
            var sp = new SuggestParameters
            {
                UseFuzzyMatching = fuzzy,
                Top = 8
            };

            return await _indexClient
                .Documents
                .SuggestAsync<Dictionary<string, object>>(searchText, AzureEventSearchExtensions.SuggesterName, sp);
        }

        public async Task<DocumentSearchResult<Dictionary<string, object>>> SearchAsync(string text, IList<string> facets = null)
        {
            var sp = new SearchParameters
            {
                SearchMode = SearchMode.All,
                Top = 20,
                Facets = facets ?? _index.FacetableFieldNames(),
                IncludeTotalResultCount = true
            };

            return await _indexClient
                .Documents
                .SearchAsync<Dictionary<string, object>>(text, sp);
        }

        public Task<long> DocumentCountAsync() => _indexClient.Documents.CountAsync();

        private async Task BatchUpdateAsync<T>(IEnumerable<T> uploadOrMerge, IEnumerable<T> upload = null, IEnumerable<T> delete = null) where T : class
        {
            var actions = new List<IndexAction<T>>();

            if (uploadOrMerge != null)
            {
                foreach (var item in uploadOrMerge)
                {
                    actions.Add(IndexAction.MergeOrUpload<T>(item));
                }
            }

            if (upload != null)
            {
                foreach (var item in upload)
                {
                    actions.Add(IndexAction.Upload<T>(item));
                }
            }

            if (delete != null)
            {
                foreach (var item in delete)
                {
                    actions.Add(IndexAction.Delete<T>(item));
                }
            }

            var batch = IndexBatch.New(actions);

            var retryStrategy =
                new IncrementalRetryStrategy(3, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));

            var retryPolicy =
                new RetryPolicy<SearchIndexErrorDetectionStrategy>(retryStrategy);
            //there is a retry policy for the client search now, we might be able to use that instead
            await retryPolicy.ExecuteAsync(async () => await _indexClient.Documents.IndexAsync(batch));
        }

        public void Dispose()
        {
            _indexClient?.Dispose();
        }

    }
}