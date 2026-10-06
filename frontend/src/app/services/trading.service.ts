import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import * as signalR from '@microsoft/signalr';
import { BehaviorSubject, Observable } from 'rxjs';

export interface StockTick {
  symbol: string;
  name: string;
  category?: string;
  price: number;
  change: number;
  changePercent: number;
  high: number;
  low: number;
  prevClose: number;
  timestamp: string;
  candleTime?: number;
  candleOpen?: number;
  candleHigh?: number;
  candleLow?: number;
  candleClose?: number;
  candleVolume?: number;
}

export interface HistoricalCandle {
  timestamp: number;
  datetime: string;
  open: number;
  high: number;
  low: number;
  close: number;
  volume: number;
}

export interface AccountInfo {
  accountId: string;
  accountName: string;
  accountType: string;
  balance: number;
  isActive?: boolean;
}

export interface ParentOrder {
  orderId: number;
  parentAccountId: string;
  symbol: string;
  orderType: string;
  price: number;
  quantity: number;
  stopLossPrice?: number | null;
  targetPrice?: number | null;
  orderStatus: string;
  placedAt: string;
  entryTime?: string;
  exitTime?: string | null;
  exitPrice?: number | null;
  realizedPnL?: number | null;
}

export interface ChildOrder {
  childOrderId: number;
  parentOrderId: number;
  childAccountId: string;
  childAccountName: string;
  symbol: string;
  orderType: string;
  price: number;
  quantity: number;
  stopLossPrice?: number | null;
  targetPrice?: number | null;
  orderStatus: string;
  replicatedAt: string;
  entryTime?: string;
  exitTime?: string | null;
  exitPrice?: number | null;
  realizedPnL?: number | null;
}

export interface AccountMapping {
  mappingId: number;
  parentAccountId: string;
  childAccountId: string;
  qtyMultiplier: number;
  isActive: boolean;
  allocationMode?: string;
  fixedQuantity?: number;
  allowedSymbols?: string;
  childAccount?: AccountInfo;
}

export interface OrderExecutionResult {
  parentOrder: ParentOrder;
  childOrders: ChildOrder[];
}

@Injectable({
  providedIn: 'root'
})
export class TradingService {
  private apiUrl = 'http://localhost:5000/api';
  private hubUrl = 'http://localhost:5000/hubs/market';
  private hubConnection!: signalR.HubConnection;

  public liveTick$ = new BehaviorSubject<StockTick | null>(null);
  public ticksMap$ = new BehaviorSubject<{ [symbol: string]: StockTick }>({});
  public orderExecuted$ = new BehaviorSubject<OrderExecutionResult | null>(null);
  public accountsUpdated$ = new BehaviorSubject<AccountInfo[] | null>(null);
  public mappingsUpdated$ = new BehaviorSubject<AccountMapping[] | null>(null);
  public isConnected$ = new BehaviorSubject<boolean>(false);

  constructor(private http: HttpClient) {
    this.initSignalR();
  }

  private initSignalR() {
    this.hubConnection = new signalR.HubConnectionBuilder()
      .withUrl(this.hubUrl)
      .withAutomaticReconnect([0, 1000, 2000, 5000, 10000])
      .build();

    this.hubConnection.onreconnecting(() => {
      console.warn('⚠️ SignalR Reconnecting to Market Hub...');
      this.isConnected$.next(false);
    });

    this.hubConnection.onreconnected((connectionId) => {
      console.log('✅ SignalR Reconnected to Market Hub:', connectionId);
      this.isConnected$.next(true);
    });

    this.hubConnection.onclose(() => {
      console.warn('❌ SignalR Market Hub Disconnected. Re-initiating connection loop...');
      this.isConnected$.next(false);
      this.startSignalRWithRetry();
    });

    this.hubConnection.on('ReceiveTick', (tick: StockTick) => {
      this.liveTick$.next(tick);
      const currentMap = { ...this.ticksMap$.value, [tick.symbol]: tick };
      this.ticksMap$.next(currentMap);
    });

    this.hubConnection.on('OrderExecuted', (result: OrderExecutionResult) => {
      this.orderExecuted$.next(result);
    });

    this.hubConnection.on('AccountsUpdated', (accounts: AccountInfo[]) => {
      this.accountsUpdated$.next(accounts);
    });

    this.hubConnection.on('MappingsUpdated', (mappings: AccountMapping[]) => {
      this.mappingsUpdated$.next(mappings);
    });

    this.startSignalRWithRetry();
  }

  private startSignalRWithRetry() {
    if (this.hubConnection.state === signalR.HubConnectionState.Connected) {
      this.isConnected$.next(true);
      return;
    }

    this.hubConnection.start()
      .then(() => {
        console.log('✅ SignalR Market Hub Connected');
        this.isConnected$.next(true);
      })
      .catch(err => {
        console.warn('⚠️ SignalR initial connection failed, retrying in 3s...', err);
        this.isConnected$.next(false);
        setTimeout(() => this.startSignalRWithRetry(), 3000);
      });
  }

  public getStocks(category?: string): Observable<StockTick[]> {
    let url = `${this.apiUrl}/stocks`;
    if (category) url += `?category=${category}`;
    return this.http.get<StockTick[]>(url);
  }

  public getStockLtp(symbol: string): Observable<any> {
    const encoded = encodeURIComponent(symbol);
    return this.http.get<any>(`${this.apiUrl}/stocks/ltp/${encoded}`);
  }

  public getChartHistory(symbol: string): Observable<any[]> {
    const encoded = encodeURIComponent(symbol);
    return this.http.get<any[]>(`${this.apiUrl}/stocks/chart-history/${encoded}`);
  }

  public getHistoricalCandles(symbol: string): Observable<HistoricalCandle[]> {
    const encoded = encodeURIComponent(symbol);
    return this.http.get<HistoricalCandle[]>(`${this.apiUrl}/stocks/historical/${encoded}`);
  }

  public getAccounts(): Observable<AccountInfo[]> {
    return this.http.get<AccountInfo[]>(`${this.apiUrl}/accounts`);
  }

  public addFunds(accountId: string, amount: number): Observable<any> {
    return this.http.post<any>(`${this.apiUrl}/accounts/add-funds`, { accountId, amount });
  }

  public setBalance(accountId: string, balance: number): Observable<any> {
    return this.http.post<any>(`${this.apiUrl}/accounts/set-balance`, { accountId, balance });
  }

  public resetDefaultFunds(): Observable<any> {
    return this.http.post<any>(`${this.apiUrl}/accounts/reset-default-funds`, {});
  }

  public getParentOrders(): Observable<ParentOrder[]> {
    return this.http.get<ParentOrder[]>(`${this.apiUrl}/orders/parent`);
  }

  public getChildOrders(childAccountId?: string): Observable<ChildOrder[]> {
    let url = `${this.apiUrl}/orders/child`;
    if (childAccountId) url += `?childAccountId=${childAccountId}`;
    return this.http.get<ChildOrder[]>(url);
  }

  public placeParentOrder(
    parentAccountId: string, 
    symbol: string, 
    orderType: string, 
    price: number, 
    quantity: number,
    stopLossPrice?: number | null,
    targetPrice?: number | null
  ): Observable<OrderExecutionResult> {
    return this.http.post<OrderExecutionResult>(`${this.apiUrl}/orders/parent`, {
      parentAccountId,
      symbol,
      orderType,
      price,
      quantity,
      stopLossPrice: stopLossPrice || null,
      targetPrice: targetPrice || null
    });
  }

  public squareOffParentOrder(orderId: number): Observable<any> {
    return this.http.post<any>(`${this.apiUrl}/orders/parent/${orderId}/square-off`, {});
  }

  public squareOffChildOrder(childOrderId: number): Observable<any> {
    return this.http.post<any>(`${this.apiUrl}/orders/child/${childOrderId}/square-off`, {});
  }

  public getMappings(): Observable<AccountMapping[]> {
    return this.http.get<AccountMapping[]>(`${this.apiUrl}/accounts/mappings`);
  }

  public toggleChildActive(childAccountId: string): Observable<any> {
    return this.http.post<any>(`${this.apiUrl}/accounts/toggle-child-active`, { childAccountId });
  }

  public updateChildSizing(childAccountId: string, multiplier?: number, allocationMode?: string, fixedQuantity?: number, allowedSymbols?: string): Observable<any> {
    return this.http.post<any>(`${this.apiUrl}/accounts/update-child-sizing`, {
      childAccountId,
      multiplier,
      allocationMode,
      fixedQuantity,
      allowedSymbols
    });
  }

  public createClient(clientData: {
    name: string;
    initialBalance?: number;
    multiplier?: number;
    allocationMode?: string;
    fixedQuantity?: number;
    allowedSymbols?: string;
  }): Observable<any> {
    return this.http.post<any>(`${this.apiUrl}/accounts/create-client`, clientData);
  }

  public updateAllowedSymbols(childAccountId: string, allowedSymbols: string): Observable<any> {
    return this.http.post<any>(`${this.apiUrl}/accounts/update-allowed-symbols`, { childAccountId, allowedSymbols });
  }

  public deleteClient(childAccountId: string): Observable<any> {
    return this.http.delete<any>(`${this.apiUrl}/accounts/delete-client/${childAccountId}`);
  }
}
