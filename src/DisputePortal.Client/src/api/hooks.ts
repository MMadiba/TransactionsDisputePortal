import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, postJson, query } from './client'
import type {
  DisputeDetail,
  DisputeFilter,
  DisputeSummary,
  OpenDisputeRequest,
  PagedResult,
  ResolveDisputeRequest,
  TransactionDetail,
  TransactionSummary,
} from './types'

// Server state lives in TanStack Query: caching, loading and error states, and refetching after a change.
// Keys are hierarchical so a mutation can invalidate everything it affects in one call.

export interface TransactionFilters {
  search?: string
  from?: string
  to?: string
  page?: number
}

export const keys = {
  transactions: ['transactions'] as const,
  transactionList: (filters: TransactionFilters) => ['transactions', 'list', filters] as const,
  transaction: (id: string) => ['transactions', 'detail', id] as const,
  disputes: ['disputes'] as const,
  disputeList: (filter: DisputeFilter, page: number) => ['disputes', 'list', filter, page] as const,
  dispute: (id: string) => ['disputes', 'detail', id] as const,
  queue: ['queue'] as const,
  queueList: (filter: DisputeFilter, page: number) => ['queue', 'list', filter, page] as const,
  review: (id: string) => ['queue', 'detail', id] as const,
}

const pageSize = 20

export function useTransactions(filters: TransactionFilters) {
  return useQuery({
    queryKey: keys.transactionList(filters),
    queryFn: () => api<PagedResult<TransactionSummary>>(`/api/transactions${query({ ...filters, pageSize })}`),
    placeholderData: keepPreviousData, // keep the table on screen while the next page loads
  })
}

export function useTransaction(id: string) {
  return useQuery({ queryKey: keys.transaction(id), queryFn: () => api<TransactionDetail>(`/api/transactions/${id}`) })
}

export function useMyDisputes(filter: DisputeFilter, page: number) {
  return useQuery({
    queryKey: keys.disputeList(filter, page),
    queryFn: () => api<PagedResult<DisputeSummary>>(`/api/disputes${query({ filter, page, pageSize })}`),
    placeholderData: keepPreviousData,
  })
}

export function useMyDispute(id: string) {
  return useQuery({ queryKey: keys.dispute(id), queryFn: () => api<DisputeDetail>(`/api/disputes/${id}`) })
}

export function useOpenDispute(transactionId: string) {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (request: OpenDisputeRequest) => postJson<DisputeDetail>(`/api/transactions/${transactionId}/disputes`, request),
    onSuccess: async (dispute) => {
      client.setQueryData(keys.dispute(dispute.id), dispute)
      await Promise.all([
        client.invalidateQueries({ queryKey: keys.transactions }),
        client.invalidateQueries({ queryKey: keys.disputes }),
      ])
    },
  })
}

export function useWithdrawDispute(id: string) {
  const client = useQueryClient()
  return useMutation({
    mutationFn: () => postJson<DisputeDetail>(`/api/disputes/${id}/withdraw`),
    onSuccess: async (dispute) => {
      client.setQueryData(keys.dispute(id), dispute)
      await Promise.all([
        client.invalidateQueries({ queryKey: keys.transactions }),
        client.invalidateQueries({ queryKey: [...keys.disputes, 'list'] }),
      ])
    },
  })
}

export function useQueue(filter: DisputeFilter, page: number) {
  return useQuery({
    queryKey: keys.queueList(filter, page),
    queryFn: () => api<PagedResult<DisputeSummary>>(`/api/agent/disputes${query({ filter, page, pageSize })}`),
    placeholderData: keepPreviousData,
  })
}

export function useReviewDispute(id: string) {
  return useQuery({ queryKey: keys.review(id), queryFn: () => api<DisputeDetail>(`/api/agent/disputes/${id}`) })
}

function useAgentCommand<TBody>(id: string, action: 'start-review' | 'resolve') {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (body: TBody) => postJson<DisputeDetail>(`/api/agent/disputes/${id}/${action}`, body),
    onSuccess: async (dispute) => {
      client.setQueryData(keys.review(id), dispute)
      await client.invalidateQueries({ queryKey: [...keys.queue, 'list'] })
    },
  })
}

export const useStartReview = (id: string) => useAgentCommand<void>(id, 'start-review')
export const useResolveDispute = (id: string) => useAgentCommand<ResolveDisputeRequest>(id, 'resolve')
