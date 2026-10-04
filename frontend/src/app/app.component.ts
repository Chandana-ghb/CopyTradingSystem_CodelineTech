import { Component, OnInit, OnDestroy, ElementRef, ViewChild, AfterViewInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TradingService, StockTick, HistoricalCandle, AccountInfo, ParentOrder, ChildOrder, OrderExecutionResult } from './services/trading.service';
import { Subscription } from 'rxjs';
import { createChart, IChartApi, ISeriesApi, CandlestickSeries } from 'lightweight-charts';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './app.component.html',
  styleUrl: './app.component.css'
})
export class AppComponent implements OnInit, OnDestroy, AfterViewInit {
  @ViewChild('chartContainer', { static: false }) chartContainer!: ElementRef;

  private chart!: IChartApi;
  private candlestickSeries!: ISeriesApi<"Candlestick">;
  private subscriptions: Subscription = new Subscription();

  public isConnected = false;
  public selectedCategory: 'NIFTY50' | 'MCX' = 'NIFTY50';
  public stocks: StockTick[] = [];
  public selectedSymbol = 'NSE:TCS-EQ';
  public activeTick: StockTick | null = null;
  public ticksMap: { [symbol: string]: StockTick } = {};

  // Dark / Light Theme Mode
  public isDarkMode = true;

  // Historical Candles Table & Tab
  public activeViewTab: 'CHART' | 'HISTORICAL_TABLE' = 'CHART';
  public historicalCandles: HistoricalCandle[] = [];
  public isLoadingCandles = false;
  public candlePage = 1;
  public candlePageSize = 100;
  public candleSortOrder: 'DESC' | 'ASC' = 'DESC';

  // Order Placement Modal
  public showOrderModal = false;
  public modalOrderType: 'BUY' | 'SELL' = 'BUY';
  public modalQuantity = 10;
  public isSubmitting = false;
  public toastMessage: string | null = null;

  // Accounts & Balances
  public parentBalance = 1000000;
  public childAccounts = [
    { id: 'C001', name: 'Ramu (Child 1)', multiplier: 1.0, balance: 500000 },
    { id: 'C002', name: 'Seenu (Child 2)', multiplier: 0.5, balance: 500000 },
    { id: 'C003', name: 'Priya (Child 3)', multiplier: 2.0, balance: 500000 },
    { id: 'C004', name: 'Arjun (Child 4)', multiplier: 1.5, balance: 500000 }
  ];

  // Order Tables
  public parentOrders: ParentOrder[] = [];
  public childOrdersMap: { [childId: string]: ChildOrder[] } = {
    'C001': [],
    'C002': [],
    'C003': [],
    'C004': []
  };

  constructor(private tradingService: TradingService) {}

  ngOnInit() {
    // 0. Theme Initialization
    this.initTheme();

    // 1. Connection Status
    this.subscriptions.add(
      this.tradingService.isConnected$.subscribe(status => this.isConnected = status)
    );

    // 2. Fetch Initial Stocks, Accounts, & Order History
    this.loadStocks();
    this.loadAccounts();
    this.loadOrders();

    // 3. Listen to Real-time SignalR Ticks
    this.subscriptions.add(
      this.tradingService.liveTick$.subscribe(tick => {
        if (!tick) return;
        this.ticksMap[tick.symbol] = tick;
        if (tick.symbol === this.selectedSymbol) {
          this.activeTick = tick;
          this.updateChartRealtime(tick);
        }
      })
    );

    // 4. Listen to Real-time SignalR Copy Execution Events
    this.subscriptions.add(
      this.tradingService.orderExecuted$.subscribe(result => {
        if (!result) return;
        this.handleNewOrderExecuted(result);
        this.loadAccounts();
      })
    );
  }

  public initTheme() {
    const savedTheme = localStorage.getItem('copytrading_theme');
    if (savedTheme) {
      this.isDarkMode = savedTheme === 'dark';
    } else {
      this.isDarkMode = true;
    }
    document.documentElement.setAttribute('data-theme', this.isDarkMode ? 'dark' : 'light');
  }

  public toggleTheme() {
    this.isDarkMode = !this.isDarkMode;
    const themeName = this.isDarkMode ? 'dark' : 'light';
    document.documentElement.setAttribute('data-theme', themeName);
    localStorage.setItem('copytrading_theme', themeName);
    this.applyChartTheme();
  }

  public applyChartTheme() {
    if (!this.chart) return;
    this.chart.applyOptions({
      layout: {
        background: { color: this.isDarkMode ? '#131722' : '#ffffff' },
        textColor: this.isDarkMode ? '#787b86' : '#64748b',
      },
      grid: {
        vertLines: { color: this.isDarkMode ? '#1e222d' : '#f1f5f9' },
        horzLines: { color: this.isDarkMode ? '#1e222d' : '#f1f5f9' },
      },
      timeScale: {
        borderColor: this.isDarkMode ? '#2a2e39' : '#e2e8f0',
      }
    });
  }

  ngAfterViewInit() {
    this.initTradingViewChart();
  }

  ngOnDestroy() {
    this.subscriptions.unsubscribe();
    if (this.chart) this.chart.remove();
  }

  public onCategoryChange(cat: 'NIFTY50' | 'MCX') {
    this.selectedCategory = cat;
    if (cat === 'MCX') {
      this.selectedSymbol = 'MCX:GOLD';
    } else {
      this.selectedSymbol = 'NSE:TCS-EQ';
    }
    this.loadStocks();
  }

  public onSymbolChange(symbol: string) {
    this.selectedSymbol = symbol;
    this.activeTick = this.ticksMap[symbol] || null;
    this.loadChartHistory(symbol);
    this.loadHistoricalCandles(symbol);
  }

  private loadStocks() {
    this.tradingService.getStocks(this.selectedCategory).subscribe(data => {
      this.stocks = data;
      data.forEach(s => this.ticksMap[s.symbol] = s);
      const found = data.find(s => s.symbol === this.selectedSymbol);
      if (found) {
        this.activeTick = found;
      } else if (data.length > 0) {
        this.selectedSymbol = data[0].symbol;
        this.activeTick = data[0];
      }
      if (this.chartContainer) {
        this.loadChartHistory(this.selectedSymbol);
        this.loadHistoricalCandles(this.selectedSymbol);
      }
    });
  }

  public loadAccounts() {
    this.tradingService.getAccounts().subscribe(accs => {
      const parent = accs.find(a => a.accountId === 'P001');
      if (parent) {
        this.parentBalance = parent.balance;
      }
      this.childAccounts.forEach(ca => {
        const matching = accs.find(a => a.accountId === ca.id);
        if (matching) {
          ca.balance = matching.balance;
        }
      });
    });
  }

  private loadOrders() {
    this.tradingService.getParentOrders().subscribe(data => {
      this.parentOrders = data;
    });

    this.tradingService.getChildOrders().subscribe(data => {
      this.groupChildOrders(data);
    });
  }

  private groupChildOrders(orders: ChildOrder[]) {
    this.childOrdersMap = { 'C001': [], 'C002': [], 'C003': [], 'C004': [] };
    orders.forEach(ord => {
      if (this.childOrdersMap[ord.childAccountId]) {
        this.childOrdersMap[ord.childAccountId].push(ord);
      }
    });
  }

  private initTradingViewChart() {
    if (!this.chartContainer) return;

    this.chart = createChart(this.chartContainer.nativeElement, {
      width: this.chartContainer.nativeElement.clientWidth,
      height: 380,
      layout: {
        background: { color: this.isDarkMode ? '#131722' : '#ffffff' },
        textColor: this.isDarkMode ? '#787b86' : '#64748b',
      },
      grid: {
        vertLines: { color: this.isDarkMode ? '#1e222d' : '#f1f5f9' },
        horzLines: { color: this.isDarkMode ? '#1e222d' : '#f1f5f9' },
      },
      crosshair: {
        mode: 1,
      },
      timeScale: {
        timeVisible: true,
        secondsVisible: true,
        borderColor: this.isDarkMode ? '#2a2e39' : '#e2e8f0',
      },
    });

    this.candlestickSeries = this.chart.addSeries(CandlestickSeries, {
      upColor: '#26a69a',
      downColor: '#ef5350',
      borderVisible: false,
      wickUpColor: '#26a69a',
      wickDownColor: '#ef5350',
    });

    this.loadChartHistory(this.selectedSymbol);
    this.loadHistoricalCandles(this.selectedSymbol);

    // Responsive Chart Resize
    window.addEventListener('resize', () => {
      if (this.chartContainer && this.chart) {
        this.chart.applyOptions({ width: this.chartContainer.nativeElement.clientWidth });
      }
    });
  }

  private loadChartHistory(symbol: string) {
    if (!this.candlestickSeries) return;

    this.tradingService.getChartHistory(symbol).subscribe(candles => {
      if (!candles || candles.length === 0) return;

      const formatted = candles.map(c => ({
        time: c.time as any,
        open: c.open,
        high: c.high,
        low: c.low,
        close: c.close
      }));

      // Sort by time ascending
      formatted.sort((a, b) => a.time - b.time);
      this.candlestickSeries.setData(formatted);
      this.chart.timeScale().fitContent();
    });
  }

  public loadHistoricalCandles(symbol: string) {
    this.isLoadingCandles = true;
    this.candlePage = 1;
    this.tradingService.getHistoricalCandles(symbol).subscribe({
      next: (data) => {
        this.historicalCandles = data;
        this.isLoadingCandles = false;

        // Sync LTP with the latest historical candle from the dataset
        if (data && data.length > 0) {
          const latestCandle = data[data.length - 1];
          const prevCandle = data.length > 1 ? data[data.length - 2] : latestCandle;

          if (this.activeTick) {
            this.activeTick.price = latestCandle.close;
            this.activeTick.high = Math.max(this.activeTick.high, latestCandle.high);
            this.activeTick.low = Math.min(this.activeTick.low, latestCandle.low);
            this.activeTick.prevClose = prevCandle.close;
            this.activeTick.change = Number((this.activeTick.price - this.activeTick.prevClose).toFixed(2));
            this.activeTick.changePercent = this.activeTick.prevClose > 0 
              ? Number(((this.activeTick.change / this.activeTick.prevClose) * 100).toFixed(2)) 
              : 0;
            this.activeTick.timestamp = latestCandle.datetime.split(' ')[1] || this.activeTick.timestamp;
          }

          const stockItem = this.stocks.find(s => s.symbol === symbol || symbol.includes(s.name));
          if (stockItem) {
            stockItem.price = latestCandle.close;
            stockItem.high = Math.max(stockItem.high, latestCandle.high);
            stockItem.low = Math.min(stockItem.low, latestCandle.low);
            stockItem.prevClose = prevCandle.close;
            stockItem.change = Number((stockItem.price - stockItem.prevClose).toFixed(2));
            stockItem.changePercent = stockItem.prevClose > 0 
              ? Number(((stockItem.change / stockItem.prevClose) * 100).toFixed(2)) 
              : 0;
          }
        }
      },
      error: () => {
        this.isLoadingCandles = false;
      }
    });
  }

  public get sortedCandles(): HistoricalCandle[] {
    if (this.candleSortOrder === 'DESC') {
      return [...this.historicalCandles].reverse();
    }
    return this.historicalCandles;
  }

  public get totalCandlePages(): number {
    return Math.ceil(this.historicalCandles.length / this.candlePageSize) || 1;
  }

  public get paginatedCandles(): HistoricalCandle[] {
    const sorted = this.sortedCandles;
    const start = (this.candlePage - 1) * this.candlePageSize;
    return sorted.slice(start, start + this.candlePageSize);
  }

  public nextCandlePage() {
    if (this.candlePage < this.totalCandlePages) {
      this.candlePage++;
    }
  }

  public prevCandlePage() {
    if (this.candlePage > 1) {
      this.candlePage--;
    }
  }

  public toggleSortOrder() {
    this.candleSortOrder = this.candleSortOrder === 'DESC' ? 'ASC' : 'DESC';
    this.candlePage = 1;
  }

  private updateChartRealtime(tick: StockTick) {
    if (!this.candlestickSeries) return;

    const nowUnix = Math.floor(Date.now() / 1000) as any;
    const price = tick.price;

    this.candlestickSeries.update({
      time: nowUnix,
      open: price,
      high: Math.max(price, tick.high),
      low: Math.min(price, tick.low),
      close: price
    });
  }

  // Modal Actions
  public openModal(type: 'BUY' | 'SELL') {
    this.modalOrderType = type;
    this.showOrderModal = true;
  }

  public closeModal() {
    this.showOrderModal = false;
  }

  public confirmOrder() {
    if (!this.activeTick || this.isSubmitting) return;

    this.isSubmitting = true;
    const price = this.activeTick.price;

    this.tradingService.placeParentOrder(
      'P001', // Parent Account (Chandana)
      this.selectedSymbol,
      this.modalOrderType,
      price,
      this.modalQuantity
    ).subscribe({
      next: (res) => {
        this.isSubmitting = false;
        this.closeModal();
        this.loadAccounts(); // Updates parent and child balances
        this.loadOrders();   // Refreshes orders from DB
        this.showToast(`✅ ${this.modalOrderType} Order #${res.parentOrder.orderId} Placed! Saved to database & replicated to ${res.childOrders.length} child accounts.`);
      },
      error: (err) => {
        this.isSubmitting = false;
        console.error('Order error:', err);
        this.showToast(`❌ Failed to place order`);
      }
    });
  }

  private handleNewOrderExecuted(res: OrderExecutionResult) {
    // Add Parent Order
    this.parentOrders = [res.parentOrder, ...this.parentOrders];

    // Add Child Orders
    res.childOrders.forEach(co => {
      if (this.childOrdersMap[co.childAccountId]) {
        this.childOrdersMap[co.childAccountId] = [co, ...this.childOrdersMap[co.childAccountId]];
      }
    });
  }

  public showToast(msg: string) {
    this.toastMessage = msg;
    setTimeout(() => {
      this.toastMessage = null;
    }, 4500);
  }
}
