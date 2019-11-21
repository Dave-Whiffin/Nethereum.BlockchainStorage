﻿using System;
using System.Threading.Tasks;

namespace Nethereum.BlockchainStore.Search
{
    public interface IIndexer<TSource> : IIndexer, IDisposable where TSource : class
    {
        Task IndexAsync(TSource source, DocumentIndexAction indexAction = DocumentIndexAction.uploadOrMerge);

        int PendingDocumentCount { get; }

        Task IndexPendingDocumentsAsync();
    }
}
