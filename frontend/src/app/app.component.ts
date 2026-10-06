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
  public currentLiveBar: { time: number; open: number; high: number; low: number; close: number } | null = null;
  public timeframeLiveBar: { time: number; open: number; high: number; low: number; close: number } | null = null;
  public raw1MinBars: { time: number; open: number; high: number; low: number; close: number }[] = [];

  public secondTimeframes = [
    { label: '5s', value: '5s' },
    { label: '15s', value: '15s' },
    { label: '30s', value: '30s' }
  ];

  public minuteTimeframes = [
    { label: '1m', value: '1m' },
    { label: '3m', value: '3m' },
    { label: '5m', value: '5m' },
    { label: '15m', value: '15m' },
    { label: '30m', value: '30m' }
  ];

  public hourTimeframes = [
    { label: '1H', value: '1H' },
    { label: '2H', value: '2H' },
    { label: '4H', value: '4H' }
  ];

  public dayTimeframes = [
    { label: '1D', value: '1D' }
  ];

  public selectedTimeframe: string = '1m';

  public get isSecondsTimeframe(): boolean {
    return this.selectedTimeframe === '5s' || this.selectedTimeframe === '15s' || this.selectedTimeframe === '30s';
  }

  public get displayBar() {
    return this.timeframeLiveBar || this.currentLiveBar;
  }

  public isConnected = false;
  public selectedCategory: 'NIFTY50' | 'MCX' = 'NIFTY50';
  public stocks: StockTick[] = [];
  public filteredStocks: StockTick[] = [];
  public stockSearchQuery = '';
  public isDropdownOpen = false;
  public selectedSymbol = 'NSE:TCS-EQ';
  public activeTick: StockTick | null = null;
  public ticksMap: { [symbol: string]: StockTick } = {};

  // Master Stocks for Child Account Search & Filtering (Both MCX and NIFTY50)
  public masterStocksList: StockTick[] = [];
  public childStockSearchQuery: { [childId: string]: string } = {};
  public childStockDropdownOpen: { [childId: string]: boolean } = {};

  // 9 Canonical MCX Commodities
  public static readonly MCX_COMMODITIES: StockTick[] = [
    { symbol: 'MCX:GOLD', name: 'GOLD', category: 'MCX', price: 0, high: 0, low: 0, prevClose: 0, change: 0, changePercent: 0, timestamp: '' },
    { symbol: 'MCX:SILVER', name: 'SILVER', category: 'MCX', price: 0, high: 0, low: 0, prevClose: 0, change: 0, changePercent: 0, timestamp: '' },
    { symbol: 'MCX:CRUDEOIL', name: 'CRUDEOIL', category: 'MCX', price: 0, high: 0, low: 0, prevClose: 0, change: 0, changePercent: 0, timestamp: '' },
    { symbol: 'MCX:NATGAS', name: 'NATGAS', category: 'MCX', price: 0, high: 0, low: 0, prevClose: 0, change: 0, changePercent: 0, timestamp: '' },
    { symbol: 'MCX:COPPER', name: 'COPPER', category: 'MCX', price: 0, high: 0, low: 0, prevClose: 0, change: 0, changePercent: 0, timestamp: '' },
    { symbol: 'MCX:ZINC', name: 'ZINC', category: 'MCX', price: 0, high: 0, low: 0, prevClose: 0, change: 0, changePercent: 0, timestamp: '' },
    { symbol: 'MCX:LEAD', name: 'LEAD', category: 'MCX', price: 0, high: 0, low: 0, prevClose: 0, change: 0, changePercent: 0, timestamp: '' },
    { symbol: 'MCX:ALUMINIUM', name: 'ALUMINIUM', category: 'MCX', price: 0, high: 0, low: 0, prevClose: 0, change: 0, changePercent: 0, timestamp: '' },
    { symbol: 'MCX:NICKEL', name: 'NICKEL', category: 'MCX', price: 0, high: 0, low: 0, prevClose: 0, change: 0, changePercent: 0, timestamp: '' }
  ];

  // Dark / Light Theme Mode
  public isDarkMode = true;

  // Live Market Timings & IST Clock
  public currentMarketTime = '';
  public currentMarketDate = '';
  public marketStatusText = '';
  public isMarketOpen = false;
  private marketClockTimer: any = null;

  // Live Candles Table & Tab
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
  public modalStopLossPrice: number | null = null;
  public modalTargetPrice: number | null = null;
  public isSubmitting = false;
  public toastMessage: string | null = null;

  // Add Funds Modal
  public showAddFundsModal = false;
  public fundTargetAccountId = 'P001';
  public fundDepositAmount = 50000;
  public isSubmittingFunds = false;

  // Accounts & Balances
  public parentBalance = 1000000;
  public childAccounts: any[] = [
    { id: 'C001', name: 'Ramu', multiplier: 0.1, balance: 500000, isActive: true, allocationMode: 'RATIO', fixedQuantity: 1, allowedSymbols: 'ALL', isUpdating: false },
    { id: 'C002', name: 'Seenu', multiplier: 0.5, balance: 500000, isActive: true, allocationMode: 'RATIO', fixedQuantity: 5, allowedSymbols: 'ALL', isUpdating: false },
    { id: 'C003', name: 'Priya', multiplier: 1.0, balance: 500000, isActive: true, allocationMode: 'RATIO', fixedQuantity: 10, allowedSymbols: 'ALL', isUpdating: false },
    { id: 'C004', name: 'Arjun', multiplier: 1.0, balance: 500000, isActive: true, allocationMode: 'RATIO', fixedQuantity: 10, allowedSymbols: 'ALL', isUpdating: false }
  ];

  public cleanAccountName(name: string): string {
    if (!name) return '';
    return name.replace(/\s*\(Child\s*\d+\)/gi, '').replace(/\s*\(Parent\)/gi, '').trim();
  }

  // Child Accounts Aggregation
  public get totalChildBalance(): number {
    return this.childAccounts.reduce((sum, c) => sum + (c.balance || 0), 0);
  }

  public get zeroFundsChildCount(): number {
    return this.childAccounts.filter(c => c.balance <= 0).length;
  }

  // Order Tables
  public parentOrders: ParentOrder[] = []; // Only the latest single order is displayed in parent client account
  public allParentOrders: ParentOrder[] = []; // Full history stored in database
  public totalParentOrdersCount = 0;

  public childOrdersMap: { [childId: string]: ChildOrder[] } = {}; // Only the latest single order is displayed per child account
  public allChildOrders: ChildOrder[] = []; // Full history stored in database
  public childOrdersDbCount: { [childId: string]: number } = {};
  public totalChildOrdersCount = 0;

  // DB History Modal
  public showDbHistoryModal = false;
  public dbHistoryTab: 'parent' | 'child' = 'parent';

  // History Search & Filter State
  public historySearchSymbol = '';
  public historyFilterDate = '';
  public historySelectedChildId = 'ALL'; // 'ALL' or specific child 'C001', 'C002', etc.

  // Create Client Modal State
  public showCreateClientModal = false;
  public newClientName = '';
  public newClientBalance = 500000;
  public newClientMultiplier = 1.0;
  public newClientMode: 'RATIO' | 'FIXED' = 'RATIO';
  public newClientFixedQty = 1;
  public newClientAllowedSymbols = 'ALL';
  public isSubmittingClient = false;

  constructor(private tradingService: TradingService) {}

  ngOnInit() {
    // 0. Theme Initialization
    this.initTheme();

    // 1. Start Live Market Clock (IST) & Market Status
    this.startMarketClock();

    // 2. Connection Status
    this.subscriptions.add(
      this.tradingService.isConnected$.subscribe(status => this.isConnected = status)
    );

    // 3. Fetch Initial Stocks, Accounts, & Order History
    this.loadStocks();
    this.loadMasterStocksList();
    this.loadAccounts();
    this.loadOrders();

    // 3. Listen to Real-time SignalR Ticks
    this.subscriptions.add(
      this.tradingService.liveTick$.subscribe(tick => {
        if (!tick) return;
        this.ticksMap[tick.symbol] = tick;

        // Update in this.stocks array
        const stockItem = this.stocks.find(s => 
          s.symbol === tick.symbol || 
          s.name.toUpperCase() === tick.name.toUpperCase()
        );
        if (stockItem) {
          stockItem.price = tick.price;
          stockItem.high = tick.high;
          stockItem.low = tick.low;
          stockItem.prevClose = tick.prevClose;
          stockItem.change = tick.change;
          stockItem.changePercent = tick.changePercent;
          stockItem.timestamp = tick.timestamp;
        }

        // Update in this.filteredStocks array for the active dropdown
        const filteredItem = this.filteredStocks.find(s => 
          s.symbol === tick.symbol || 
          s.name.toUpperCase() === tick.name.toUpperCase()
        );
        if (filteredItem) {
          filteredItem.price = tick.price;
          filteredItem.high = tick.high;
          filteredItem.low = tick.low;
          filteredItem.prevClose = tick.prevClose;
          filteredItem.change = tick.change;
          filteredItem.changePercent = tick.changePercent;
          filteredItem.timestamp = tick.timestamp;
        }

        const isMatch = tick.symbol === this.selectedSymbol || 
          tick.name.toUpperCase() === this.selectedSymbol.replace('MCX:', '').replace('NSE:', '').replace('-EQ', '').toUpperCase();
        if (isMatch) {
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

    // 5. Listen to Real-time SignalR Account Balance Updates
    this.subscriptions.add(
      this.tradingService.accountsUpdated$.subscribe(accs => {
        if (!accs) return;
        const parent = accs.find(a => a.accountId === 'P001');
        if (parent) {
          this.parentBalance = parent.balance;
        }
        this.syncChildAccountsWithData(accs);
      })
    );

    // 6. Listen to Real-time SignalR Child Mappings Updates
    this.subscriptions.add(
      this.tradingService.mappingsUpdated$.subscribe(mappings => {
        if (mappings) this.updateChildMappingsFromData(mappings);
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
    if (this.marketClockTimer) {
      clearInterval(this.marketClockTimer);
    }
  }

  private startMarketClock() {
    this.updateMarketClock();
    this.marketClockTimer = setInterval(() => {
      this.updateMarketClock();
    }, 1000);
  }

  public updateMarketClock() {
    try {
      const now = new Date();
      // Format IST Timezone accurately without local double-offset distortion
      const istDateStr = now.toLocaleDateString('en-GB', { timeZone: 'Asia/Kolkata', day: '2-digit', month: 'short', year: 'numeric' });
      const istTimeStr = now.toLocaleTimeString('en-GB', { timeZone: 'Asia/Kolkata', hour12: false });

      this.currentMarketDate = istDateStr.replace(/ /g, '-');
      this.currentMarketTime = istTimeStr;

      const timeParts = istTimeStr.split(':').map(Number);
      const hours = timeParts[0] || 0;
      const minutes = timeParts[1] || 0;
      const currentMinutes = hours * 60 + minutes;

      const dayFormatter = new Intl.DateTimeFormat('en-US', { timeZone: 'Asia/Kolkata', weekday: 'short' });
      const dayName = dayFormatter.format(now);
      const isWeekday = !['Sat', 'Sun'].includes(dayName);

      if (this.selectedCategory === 'MCX') {
        // MCX Live Market: 09:00 AM (540 mins) to 11:30 PM (1410 mins)
        const mcxOpenMinutes = 9 * 60;
        const mcxCloseMinutes = 23 * 60 + 30;
        const isOfficialOpen = isWeekday && currentMinutes >= mcxOpenMinutes && currentMinutes <= mcxCloseMinutes;

        this.isMarketOpen = isOfficialOpen;
        if (isOfficialOpen) {
          this.marketStatusText = 'MCX LIVE (09:00 - 23:30)';
        } else {
          this.marketStatusText = isWeekday ? 'MCX CLOSED (09:00 - 23:30)' : 'MCX WEEKEND (CLOSED)';
        }
      } else {
        // NIFTY50 / NSE Live Market: 09:15 AM (555 mins) to 03:30 PM (930 mins)
        const nseOpenMinutes = 9 * 60 + 15;
        const nseCloseMinutes = 15 * 60 + 30;
        const isOfficialOpen = isWeekday && currentMinutes >= nseOpenMinutes && currentMinutes <= nseCloseMinutes;

        this.isMarketOpen = isOfficialOpen;
        if (isOfficialOpen) {
          this.marketStatusText = 'NIFTY50 LIVE (09:15 - 15:30)';
        } else {
          this.marketStatusText = isWeekday ? 'NIFTY50 CLOSED (09:15 - 15:30)' : 'NIFTY50 WEEKEND (CLOSED)';
        }
      }
    } catch {
      this.isMarketOpen = false;
      this.marketStatusText = 'MARKET CLOSED';
    }
  }

  public onCategoryChange(cat: 'NIFTY50' | 'MCX') {
    this.selectedCategory = cat;
    this.stockSearchQuery = '';
    this.isDropdownOpen = false;
    this.updateMarketClock();
    if (cat === 'MCX') {
      this.selectedSymbol = 'MCX:GOLD';
    } else {
      this.selectedSymbol = 'NSE:TCS-EQ';
    }
    this.loadStocks();
  }

  public onSearchChange(query: string) {
    this.stockSearchQuery = query;
    this.isDropdownOpen = true; // Automatically open dropdown as user types
    this.filterStocks();
  }

  public clearSearch() {
    this.stockSearchQuery = '';
    this.filterStocks();
  }

  public toggleDropdown() {
    this.isDropdownOpen = !this.isDropdownOpen;
    if (this.isDropdownOpen) {
      this.filterStocks();
    }
  }

  public openDropdown() {
    this.isDropdownOpen = true;
    this.filterStocks();
  }

  public closeDropdown() {
    setTimeout(() => {
      this.isDropdownOpen = false;
    }, 150);
  }

  public selectStockItem(stock: StockTick) {
    this.selectedSymbol = stock.symbol;
    this.activeTick = stock;
    this.ticksMap[stock.symbol] = stock;
    this.stockSearchQuery = '';
    this.isDropdownOpen = false;
    this.onSymbolChange(stock.symbol);
  }

  public filterStocks() {
    if (!this.stockSearchQuery || this.stockSearchQuery.trim() === '') {
      this.filteredStocks = [...this.stocks];
      return;
    }
    const q = this.stockSearchQuery.toLowerCase().trim();
    this.filteredStocks = this.stocks.filter(s => 
      s.name.toLowerCase().includes(q) || 
      s.symbol.toLowerCase().includes(q)
    );
  }

  public onSymbolChange(symbol: string) {
    this.selectedSymbol = symbol;
    const found = this.ticksMap[symbol] || this.stocks.find(s => s.symbol === symbol || s.name === symbol);
    if (found) {
      this.activeTick = found;
    }
    this.loadChartHistory(symbol);
    this.loadHistoricalCandles(symbol);
  }

  public onSwitchToChart() {
    this.activeViewTab = 'CHART';
    setTimeout(() => {
      if (this.chart && this.chartContainer) {
        this.chart.applyOptions({ width: this.chartContainer.nativeElement.clientWidth });
        this.chart.timeScale().fitContent();
      }
    }, 60);
  }

  private loadStocks() {
    this.tradingService.getStocks(this.selectedCategory).subscribe({
      next: (data) => {
        let categoryStocks = data || [];
        if (this.selectedCategory === 'MCX') {
          // Guarantee strictly the 9 MCX stocks only
          categoryStocks = categoryStocks.filter(s => s.category?.toUpperCase() === 'MCX' || s.symbol.startsWith('MCX:'));
          if (categoryStocks.length === 0) {
            categoryStocks = [...AppComponent.MCX_COMMODITIES];
          }
        } else {
          // Guarantee strictly NIFTY 50 stocks
          categoryStocks = categoryStocks.filter(s => s.category?.toUpperCase() === 'NIFTY50' || s.symbol.startsWith('NSE:'));
        }

        this.stocks = categoryStocks;
        this.stocks.forEach(s => this.ticksMap[s.symbol] = s);
        this.filterStocks();

        const found = this.stocks.find(s => s.symbol === this.selectedSymbol);
        if (found) {
          this.activeTick = found;
        } else if (this.stocks.length > 0) {
          this.selectedSymbol = this.stocks[0].symbol;
          this.activeTick = this.stocks[0];
        }

        if (this.chartContainer) {
          this.loadChartHistory(this.selectedSymbol);
          this.loadHistoricalCandles(this.selectedSymbol);
        }
      },
      error: (err) => {
        console.warn('Error loading stocks, using fallback data:', err);
        if (this.selectedCategory === 'MCX') {
          this.stocks = [...AppComponent.MCX_COMMODITIES];
        }
        this.stocks.forEach(s => this.ticksMap[s.symbol] = s);
        this.filterStocks();

        if (this.stocks.length > 0) {
          this.selectedSymbol = this.stocks[0].symbol;
          this.activeTick = this.stocks[0];
          if (this.chartContainer) {
            this.loadChartHistory(this.selectedSymbol);
            this.loadHistoricalCandles(this.selectedSymbol);
          }
        }
      }
    });
  }

  public loadAccounts() {
    this.tradingService.getAccounts().subscribe(accs => {
      if (!accs) return;
      const parent = accs.find(a => a.accountId === 'P001');
      if (parent) {
        this.parentBalance = parent.balance;
      }
      this.syncChildAccountsWithData(accs);
    });

    this.tradingService.getMappings().subscribe({
      next: (mappings) => {
        if (mappings && mappings.length > 0) {
          this.updateChildMappingsFromData(mappings);
        }
      },
      error: (err) => {
        console.warn('Could not fetch mappings from DB:', err);
      }
    });
  }

  public syncChildAccountsWithData(accs: any[]) {
    if (!accs) return;
    const childs = accs.filter(a => a.accountType === 'CHILD');
    const childIds = new Set(childs.map(c => c.accountId));

    // Remove accounts deleted from DB
    this.childAccounts = this.childAccounts.filter(ca => childIds.has(ca.id));

    childs.forEach(c => {
      let existing = this.childAccounts.find(ca => ca.id === c.accountId);
      if (!existing) {
        existing = {
          id: c.accountId,
          name: this.cleanAccountName(c.accountName),
          multiplier: 1.0,
          balance: c.balance,
          isActive: c.isActive !== undefined ? c.isActive : true,
          allocationMode: 'RATIO',
          fixedQuantity: 1,
          allowedSymbols: 'ALL',
          isUpdating: false
        };
        this.childAccounts.push(existing);
        if (!this.childOrdersMap[c.accountId]) {
          this.childOrdersMap[c.accountId] = [];
          this.childOrdersDbCount[c.accountId] = 0;
        }
      } else {
        existing.balance = c.balance;
        if (c.isActive !== undefined && c.isActive !== null) existing.isActive = c.isActive;
        if (c.accountName) existing.name = this.cleanAccountName(c.accountName);
      }
    });

    this.childAccounts.sort((a, b) => a.id.localeCompare(b.id));
  }

  public removeClient(child: any) {
    if (!child || !child.id) return;
    if (child.id === 'P001') {
      this.showToastNotification('Cannot delete parent account.');
      return;
    }

    if (!confirm(`Are you sure you want to remove client "${child.name} (${child.id})"?\n\nThis will permanently delete this client and all their mappings from the database.`)) {
      return;
    }

    child.isUpdating = true;
    this.tradingService.deleteClient(child.id).subscribe({
      next: (res) => {
        child.isUpdating = false;
        this.showToastNotification(`🗑️ ${res?.message || 'Client account removed successfully.'}`);
        this.loadAccounts();
        this.loadOrders();
      },
      error: (err) => {
        child.isUpdating = false;
        this.showToastNotification(`❌ Failed to delete client: ${err.error?.message || err.message}`);
      }
    });
  }

  public updateChildMappingsFromData(mappings: any[]) {
    if (!mappings || mappings.length === 0) return;
    this.childAccounts.forEach(ca => {
      const map = mappings.find((m: any) => m.childAccountId === ca.id);
      if (map) {
        if (map.isActive !== undefined && map.isActive !== null) {
          ca.isActive = map.isActive === true || map.isActive === 1 || map.isActive === 'true';
        }
        if (map.qtyMultiplier !== undefined && map.qtyMultiplier !== null) {
          ca.multiplier = Number(map.qtyMultiplier);
        }
        if (map.allocationMode) {
          ca.allocationMode = map.allocationMode.toUpperCase();
        }
        if (map.fixedQuantity !== undefined && map.fixedQuantity !== null) {
          ca.fixedQuantity = Number(map.fixedQuantity);
        }
        if (map.allowedSymbols !== undefined && map.allowedSymbols !== null) {
          ca.allowedSymbols = map.allowedSymbols.trim() || 'ALL';
        }
      }
    });
  }

  public toggleChildStatus(child: any) {
    child.isUpdating = true;
    this.tradingService.toggleChildActive(child.id).subscribe({
      next: (res) => {
        child.isUpdating = false;
        if (res?.mapping?.isActive !== undefined) {
          child.isActive = res.mapping.isActive;
        } else {
          child.isActive = !child.isActive;
        }
        if (res?.mappings) {
          this.updateChildMappingsFromData(res.mappings);
        }
        this.showToastNotification(`Account ${child.name} is now ${child.isActive ? 'ACTIVE' : 'DEACTIVATED'} (Saved in Database)`);
      },
      error: (err) => {
        child.isUpdating = false;
        this.showToastNotification(`Failed to save status for ${child.name}`);
      }
    });
  }

  public saveChildSizing(child: any) {
    child.isUpdating = true;
    this.tradingService.updateChildSizing(child.id, child.multiplier, child.allocationMode, child.fixedQuantity, child.allowedSymbols).subscribe({
      next: (res) => {
        child.isUpdating = false;
        if (res?.mapping) {
          child.multiplier = Number(res.mapping.qtyMultiplier);
          child.allocationMode = res.mapping.allocationMode;
          child.fixedQuantity = Number(res.mapping.fixedQuantity);
          if (res.mapping.allowedSymbols) child.allowedSymbols = res.mapping.allowedSymbols;
        }
        if (res?.mappings) {
          this.updateChildMappingsFromData(res.mappings);
        }
        const info = child.allocationMode === 'FIXED' ? `${child.fixedQuantity} Fixed Lots` : `${child.multiplier}x Multiplier`;
        this.showToastNotification(`Updated sizing for ${child.name} to ${info} (Saved in Database)`);
      },
      error: (err) => {
        child.isUpdating = false;
        this.showToastNotification(`Failed to save sizing for ${child.name}`);
      }
    });
  }

  public saveChildAllowedSymbols(child: any) {
    child.isUpdating = true;
    const syms = child.allowedSymbols?.trim() || 'ALL';
    this.tradingService.updateAllowedSymbols(child.id, syms).subscribe({
      next: (res) => {
        child.isUpdating = false;
        this.showToastNotification(`Allowed symbols for ${child.name} set to: '${syms}' (Saved in DB)`);
      },
      error: (err) => {
        child.isUpdating = false;
        this.showToastNotification(`Failed to save allowed symbols for ${child.name}`);
      }
    });
  }

  public setChildAllowedSymbolPreset(child: any, preset: string) {
    child.allowedSymbols = preset;
    this.saveChildAllowedSymbols(child);
  }

  public loadMasterStocksList() {
    this.tradingService.getStocks().subscribe({
      next: (stocks) => {
        if (stocks && stocks.length > 0) {
          this.masterStocksList = stocks;
        } else {
          this.masterStocksList = [...AppComponent.MCX_COMMODITIES];
        }
      },
      error: () => {
        this.masterStocksList = [...AppComponent.MCX_COMMODITIES];
      }
    });
  }

  public getFilteredStocksForChild(childId: string): StockTick[] {
    const query = (this.childStockSearchQuery[childId] || '').trim().toLowerCase();
    const list = this.masterStocksList.length > 0 ? this.masterStocksList : AppComponent.MCX_COMMODITIES;
    if (!query) {
      return list.slice(0, 10);
    }
    return list.filter(s => 
      s.name.toLowerCase().includes(query) || 
      s.symbol.toLowerCase().includes(query)
    ).slice(0, 12);
  }

  public toggleChildStockDropdown(childId: string) {
    this.childStockDropdownOpen[childId] = !this.childStockDropdownOpen[childId];
  }

  public openChildStockDropdown(childId: string) {
    this.childStockDropdownOpen[childId] = true;
  }

  public closeChildStockDropdown(childId: string) {
    setTimeout(() => {
      this.childStockDropdownOpen[childId] = false;
    }, 250);
  }

  public cleanStockToken(str: string): string {
    if (!str) return '';
    return str
      .replace(/^NSE:/i, '')
      .replace(/^MCX:/i, '')
      .replace(/-EQ$/i, '')
      .replace(/^BLOCK:/i, '')
      .replace(/^NOT:/i, '')
      .replace(/^!/i, '')
      .trim();
  }

  public allowStockForChild(child: any, stockNameOrSymbol: string) {
    const target = this.cleanStockToken(stockNameOrSymbol).toUpperCase();
    if (!target) return;

    let current = child.allowedSymbols ? child.allowedSymbols.trim() : 'ALL';
    if (current === 'ALL' || !current) {
      child.allowedSymbols = target;
    } else {
      let tokens = current.split(',').map((t: string) => t.trim()).filter((t: string) => t);
      tokens = tokens.filter((t: string) => this.cleanStockToken(t).toUpperCase() !== target);
      tokens.push(target);
      child.allowedSymbols = tokens.join(', ');
    }

    this.childStockSearchQuery[child.id] = '';
    this.childStockDropdownOpen[child.id] = false;
    this.saveChildAllowedSymbols(child);
  }

  public blockStockForChild(child: any, stockNameOrSymbol: string) {
    const target = this.cleanStockToken(stockNameOrSymbol).toUpperCase();
    if (!target) return;

    let current = child.allowedSymbols ? child.allowedSymbols.trim() : 'ALL';
    let tokens: string[] = [];

    if (current === 'ALL' || !current) {
      tokens = [`BLOCK:${target}`];
    } else {
      tokens = current.split(',').map((t: string) => t.trim()).filter((t: string) => t);
      tokens = tokens.filter((t: string) => this.cleanStockToken(t).toUpperCase() !== target);
      tokens.push(`BLOCK:${target}`);
    }

    child.allowedSymbols = tokens.join(', ');
    this.childStockSearchQuery[child.id] = '';
    this.childStockDropdownOpen[child.id] = false;
    this.saveChildAllowedSymbols(child);
  }

  public removeChildSymbolRule(child: any, ruleToRemove: string) {
    let current = child.allowedSymbols ? child.allowedSymbols.trim() : 'ALL';
    let tokens = current.split(',').map((t: string) => t.trim()).filter((t: string) => t);
    tokens = tokens.filter((t: string) => t.toUpperCase() !== ruleToRemove.toUpperCase());

    child.allowedSymbols = tokens.length > 0 ? tokens.join(', ') : 'ALL';
    this.saveChildAllowedSymbols(child);
  }

  public getChildParsedRules(allowedSymbols: string): { raw: string; isBlock: boolean; name: string }[] {
    const str = (allowedSymbols || 'ALL').trim();
    if (str === 'ALL') {
      return [{ raw: 'ALL', isBlock: false, name: 'ALL SYMBOLS' }];
    }
    const tokens = str.split(',').map(t => t.trim()).filter(t => t);
    return tokens.map(t => {
      const isBlock = t.startsWith('!') || t.toUpperCase().startsWith('BLOCK:') || t.toUpperCase().startsWith('NOT:');
      const name = this.cleanStockToken(t).toUpperCase();
      return { raw: t, isBlock, name };
    });
  }

  public openCreateClientModal() {
    this.newClientName = '';
    this.newClientBalance = 500000;
    this.newClientMultiplier = 1.0;
    this.newClientMode = 'RATIO';
    this.newClientFixedQty = 1;
    this.newClientAllowedSymbols = 'ALL';
    this.showCreateClientModal = true;
  }

  public closeCreateClientModal() {
    this.showCreateClientModal = false;
  }

  public submitCreateClient() {
    if (!this.newClientName || !this.newClientName.trim()) {
      this.showToastNotification('⚠️ Please enter a client name.');
      return;
    }
    this.isSubmittingClient = true;
    this.tradingService.createClient({
      name: this.newClientName.trim(),
      initialBalance: this.newClientBalance,
      multiplier: this.newClientMultiplier,
      allocationMode: this.newClientMode,
      fixedQuantity: this.newClientFixedQty,
      allowedSymbols: this.newClientAllowedSymbols.trim()
    }).subscribe({
      next: (res) => {
        this.isSubmittingClient = false;
        this.closeCreateClientModal();
        this.loadAccounts();
        this.showToastNotification(`✅ ${res?.message || 'Client created successfully!'}`);
      },
      error: (err) => {
        this.isSubmittingClient = false;
        this.showToastNotification(`❌ Failed to create client: ${err?.error?.message || err?.message || ''}`);
      }
    });
  }

  // History Filter Getters
  public get filteredParentOrders(): ParentOrder[] {
    return this.allParentOrders.filter(ord => {
      const matchSymbol = !this.historySearchSymbol || 
        ord.symbol.toLowerCase().includes(this.historySearchSymbol.toLowerCase().trim());
      
      const matchDate = !this.historyFilterDate || 
        this.isSameDate(ord.placedAt, this.historyFilterDate);

      return matchSymbol && matchDate;
    });
  }

  public get filteredChildOrders(): ChildOrder[] {
    return this.allChildOrders.filter(ord => {
      const matchChild = this.historySelectedChildId === 'ALL' || 
        ord.childAccountId === this.historySelectedChildId;

      const matchSymbol = !this.historySearchSymbol || 
        ord.symbol.toLowerCase().includes(this.historySearchSymbol.toLowerCase().trim());

      const matchDate = !this.historyFilterDate || 
        this.isSameDate(ord.replicatedAt, this.historyFilterDate);

      return matchChild && matchSymbol && matchDate;
    });
  }

  public isSameDate(dateStr: string | undefined, filterDate: string): boolean {
    if (!dateStr || !filterDate) return false;
    try {
      const d = new Date(dateStr);
      const yyyy = d.getFullYear();
      const mm = String(d.getMonth() + 1).padStart(2, '0');
      const dd = String(d.getDate()).padStart(2, '0');
      const formatted = `${yyyy}-${mm}-${dd}`;
      return formatted === filterDate;
    } catch {
      return false;
    }
  }

  public clearHistoryFilters() {
    this.historySearchSymbol = '';
    this.historyFilterDate = '';
    this.historySelectedChildId = 'ALL';
  }

  public setAllocationMode(child: any, mode: string) {
    child.allocationMode = mode;
    this.saveChildSizing(child);
  }

  public adjustMultiplier(child: any, delta: number) {
    const current = Number(child.multiplier) || 1.0;
    const nextVal = Math.max(0.05, Math.min(50, Math.round((current + delta) * 100) / 100));
    child.multiplier = nextVal;
    this.saveChildSizing(child);
  }

  public adjustFixedLots(child: any, delta: number) {
    const current = Number(child.fixedQuantity) || 1;
    const nextVal = Math.max(1, Math.min(500, current + delta));
    child.fixedQuantity = nextVal;
    this.saveChildSizing(child);
  }

  public adjustModalQuantity(delta: number) {
    this.modalQuantity = Math.max(1, Math.min(10000, (this.modalQuantity || 1) + delta));
  }

  public setModalQuantity(qty: number) {
    this.modalQuantity = qty;
  }

  public manualSquareOff(orderId: number) {
    if (!confirm(`Are you sure you want to Square Off Order #${orderId} at current market price?`)) return;
    this.tradingService.squareOffParentOrder(orderId).subscribe({
      next: (res) => {
        this.showToastNotification(`Order #${orderId} squared off at current market price!`);
        this.loadOrders();
        this.loadAccounts();
      },
      error: (err) => {
        this.showToastNotification(`Failed to square off order: ${err.error?.message || err.message}`);
      }
    });
  }

  public manualSquareOffChild(childOrder: any) {
    if (!childOrder || !childOrder.childOrderId) return;
    const clientName = childOrder.childAccountName || childOrder.childAccountId;
    if (!confirm(`Are you sure you want to execute Exit Signal for ${clientName} (${childOrder.symbol}) at current market price?`)) return;

    this.tradingService.squareOffChildOrder(childOrder.childOrderId).subscribe({
      next: (res) => {
        this.showToastNotification(`✅ Position for ${clientName} closed! Margin and Realized PnL returned.`);
        this.loadOrders();
        this.loadAccounts();
      },
      error: (err) => {
        this.showToastNotification(`❌ Failed to exit child order: ${err.error?.message || err.message}`);
      }
    });
  }

  public getOrderPnL(ord: any): { value: number; isProfit: boolean; isClosed: boolean } {
    if (!ord) return { value: 0, isProfit: true, isClosed: true };
    if (ord.realizedPnL !== null && ord.realizedPnL !== undefined) {
      return {
        value: ord.realizedPnL,
        isProfit: ord.realizedPnL >= 0,
        isClosed: true
      };
    }
    if (ord.orderStatus === 'EXECUTED' && ord.quantity > 0) {
      const currentPrice = this.ticksMap[ord.symbol]?.price || this.activeTick?.price || ord.price;
      const pnl = ord.orderType === 'BUY'
        ? (currentPrice - ord.price) * ord.quantity
        : (ord.price - currentPrice) * ord.quantity;
      return {
        value: pnl,
        isProfit: pnl >= 0,
        isClosed: false
      };
    }
    return { value: 0, isProfit: true, isClosed: true };
  }

  public showToastNotification(msg: string) {
    this.toastMessage = msg;
    setTimeout(() => {
      if (this.toastMessage === msg) this.toastMessage = null;
    }, 4000);
  }

  public openDbHistoryModal(tab: 'parent' | 'child' = 'parent') {
    this.dbHistoryTab = tab;
    this.clearHistoryFilters();
    this.showDbHistoryModal = true;
    this.loadOrders();
  }

  public closeDbHistoryModal() {
    this.showDbHistoryModal = false;
  }

  private loadOrders() {
    this.tradingService.getParentOrders().subscribe(data => {
      this.allParentOrders = data || [];
      this.totalParentOrdersCount = this.allParentOrders.filter(o => !o.orderStatus.includes('REJECTED')).length;
      // ONLY the latest one order is displayed in parent client account
      this.parentOrders = this.allParentOrders.length > 0 ? [this.allParentOrders[0]] : [];
    });

    this.tradingService.getChildOrders().subscribe(data => {
      this.allChildOrders = data || [];
      // Only count non-rejected orders (e.g. 55 instead of 56 when 1 is rejected)
      this.totalChildOrdersCount = this.allChildOrders.filter(o => !o.orderStatus.includes('REJECTED')).length;
      this.groupChildOrders(this.allChildOrders);
    });
  }

  private groupChildOrders(orders: ChildOrder[]) {
    this.childOrdersMap = {};
    this.childOrdersDbCount = {};
    this.childAccounts.forEach(c => {
      this.childOrdersMap[c.id] = [];
      this.childOrdersDbCount[c.id] = 0;
    });

    orders.forEach(ord => {
      if (this.childOrdersDbCount[ord.childAccountId] !== undefined) {
        if (!ord.orderStatus.includes('REJECTED')) {
          this.childOrdersDbCount[ord.childAccountId]++;
        }
      } else {
        this.childOrdersDbCount[ord.childAccountId] = ord.orderStatus.includes('REJECTED') ? 0 : 1;
      }
    });

    // ONLY the latest one order is displayed for each child account!
    const seen = new Set<string>();
    orders.forEach(ord => {
      if (!this.childOrdersMap[ord.childAccountId]) {
        this.childOrdersMap[ord.childAccountId] = [];
      }
      if (!seen.has(ord.childAccountId)) {
        this.childOrdersMap[ord.childAccountId] = [ord];
        seen.add(ord.childAccountId);
      }
    });
  }

  public get liveBarDateFormatted(): string {
    if (this.currentLiveBar && this.currentLiveBar.time > 0) {
      const d = new Date(this.currentLiveBar.time * 1000);
      const istOffsetMs = 5.5 * 3600000;
      const istDate = new Date(d.getTime() + istOffsetMs);
      const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
      const dd = String(istDate.getUTCDate()).padStart(2, '0');
      const mon = months[istDate.getUTCMonth()];
      const yyyy = istDate.getUTCFullYear();
      return `${dd}-${mon}-${yyyy}`;
    }
    return this.currentMarketDate || '--';
  }

  public get liveBarTimeFormatted(): string {
    if (this.currentLiveBar && this.currentLiveBar.time > 0) {
      const d = new Date(this.currentLiveBar.time * 1000);
      const istOffsetMs = 5.5 * 3600000;
      const istDate = new Date(d.getTime() + istOffsetMs);
      const pad = (n: number) => String(n).padStart(2, '0');
      return `${pad(istDate.getUTCHours())}:${pad(istDate.getUTCMinutes())}:${pad(istDate.getUTCSeconds())}`;
    }
    return this.currentMarketTime || '--:--:--';
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
      localization: {
        dateFormat: 'dd-MMM-yyyy',
        timeFormatter: (time: number) => {
          const d = new Date(time * 1000);
          const istOffsetMs = 5.5 * 3600000;
          const istDate = new Date(d.getTime() + istOffsetMs);
          const pad = (n: number) => String(n).padStart(2, '0');
          const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
          const dd = pad(istDate.getUTCDate());
          const mon = months[istDate.getUTCMonth()];
          const yyyy = istDate.getUTCFullYear();
          const hh = pad(istDate.getUTCHours());
          const mm = pad(istDate.getUTCMinutes());
          const ss = pad(istDate.getUTCSeconds());
          return `${dd}-${mon}-${yyyy} ${hh}:${mm}:${ss} IST`;
        }
      },
      timeScale: {
        timeVisible: true,
        secondsVisible: this.isSecondsTimeframe,
        borderColor: this.isDarkMode ? '#2a2e39' : '#e2e8f0',
        barSpacing: 9,
        minBarSpacing: 3,
        rightOffset: 6,
        tickMarkFormatter: (time: number) => {
          const d = new Date(time * 1000);
          const istOffsetMs = 5.5 * 3600000;
          const istDate = new Date(d.getTime() + istOffsetMs);
          const pad = (n: number) => String(n).padStart(2, '0');
          const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
          const dd = pad(istDate.getUTCDate());
          const mon = months[istDate.getUTCMonth()];
          const hh = pad(istDate.getUTCHours());
          const mm = pad(istDate.getUTCMinutes());
          const ss = pad(istDate.getUTCSeconds());
          if (this.isSecondsTimeframe) {
            return `${hh}:${mm}:${ss}`;
          }
          if (this.selectedTimeframe === '1D') {
            return `${dd} ${mon}`;
          }
          return `${hh}:${mm}`;
        }
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

  public getTodayMarketOpenEpoch(symbol: string): number {
    const isMcx = symbol.startsWith('MCX:') || this.selectedCategory === 'MCX';
    const istOffsetMs = 5.5 * 3600000;
    const istDate = new Date(Date.now() + istOffsetMs);
    const y = istDate.getUTCFullYear();
    const m = istDate.getUTCMonth();
    const d = istDate.getUTCDate();
    // NSE market opens at 09:15 IST (03:45 UTC). MCX market opens at 09:00 IST (03:30 UTC).
    const utcH = 3;
    const utcM = isMcx ? 30 : 45;
    return Math.floor(Date.UTC(y, m, d, utcH, utcM, 0) / 1000);
  }

  public getTimeframeSeconds(tf: string): number {
    switch (tf) {
      case '5s': return 5;
      case '15s': return 15;
      case '30s': return 30;
      case '1m': return 60;
      case '3m': return 3 * 60;
      case '5m': return 5 * 60;
      case '15m': return 15 * 60;
      case '30m': return 30 * 60;
      case '1H': return 60 * 60;
      case '2H': return 2 * 60 * 60;
      case '4H': return 4 * 60 * 60;
      case '1D': return 24 * 60 * 60;
      default: return 60;
    }
  }

  public setTimeframe(tf: string) {
    if (this.selectedTimeframe === tf) return;
    this.selectedTimeframe = tf;

    if (this.chart) {
      this.chart.applyOptions({
        timeScale: {
          secondsVisible: this.isSecondsTimeframe
        }
      });
    }

    this.renderChartForTimeframe();
  }

  private aggregateBars(bars: { time: number; open: number; high: number; low: number; close: number }[], tf: string) {
    if (!bars || bars.length === 0) {
      return [];
    }

    if (tf === '1m') {
      return bars;
    }

    const bucketSeconds = this.getTimeframeSeconds(tf);

    // If sub-minute seconds timeframe (5s, 15s, 30s)
    if (this.isSecondsTimeframe) {
      const result: { time: number; open: number; high: number; low: number; close: number }[] = [];
      const subPerMinute = 60 / bucketSeconds;

      for (const b of bars) {
        const step = (b.close - b.open) / subPerMinute;
        for (let i = 0; i < subPerMinute; i++) {
          const subTime = b.time + (i * bucketSeconds);
          const subOpen = Number((b.open + (i * step)).toFixed(2));
          const subClose = Number((b.open + ((i + 1) * step)).toFixed(2));
          const subHigh = Number(Math.max(subOpen, subClose, i === 0 ? b.high : subOpen).toFixed(2));
          const subLow = Number(Math.min(subOpen, subClose, i === subPerMinute - 1 ? b.low : subOpen).toFixed(2));
          result.push({
            time: subTime,
            open: subOpen,
            high: subHigh,
            low: subLow,
            close: subClose
          });
        }
      }
      return result.sort((a, b) => a.time - b.time);
    }

    const istOffset = 19800; // 5.5 hours IST offset
    const bucketMap = new Map<number, { time: number; open: number; high: number; low: number; close: number }>();

    for (const b of bars) {
      const bucketTime = Math.floor((b.time + istOffset) / bucketSeconds) * bucketSeconds - istOffset;
      const existing = bucketMap.get(bucketTime);
      if (!existing) {
        bucketMap.set(bucketTime, {
          time: bucketTime,
          open: b.open,
          high: b.high,
          low: b.low,
          close: b.close
        });
      } else {
        existing.high = Math.max(existing.high, b.high);
        existing.low = Math.min(existing.low, b.low);
        existing.close = b.close;
      }
    }

    return Array.from(bucketMap.values()).sort((a, b) => a.time - b.time);
  }

  private renderChartForTimeframe() {
    if (!this.candlestickSeries || !this.raw1MinBars || this.raw1MinBars.length === 0) return;

    const aggregated = this.aggregateBars(this.raw1MinBars, this.selectedTimeframe);
    this.candlestickSeries.setData(aggregated as any);

    if (aggregated.length > 0) {
      const last = aggregated[aggregated.length - 1];
      this.timeframeLiveBar = {
        time: last.time,
        open: last.open,
        high: last.high,
        low: last.low,
        close: last.close
      };

      const count = aggregated.length;
      const visibleBars = Math.min(count, 80);
      this.chart.timeScale().setVisibleLogicalRange({
        from: Math.max(0, count - visibleBars),
        to: count + 4
      });
    }
    this.chart.timeScale().scrollToRealTime();
  }

  private loadChartHistory(symbol: string) {
    if (!this.candlestickSeries) return;
    this.currentLiveBar = null;
    this.timeframeLiveBar = null;

    this.tradingService.getChartHistory(symbol).subscribe({
      next: (candles) => {
        if (!candles || candles.length === 0) return;

        const formatted = candles.map(c => ({
          time: c.time as any,
          open: c.open,
          high: c.high,
          low: c.low,
          close: c.close
        }));

        // Sort by time ascending and strictly deduplicate timestamps for TradingView
        formatted.sort((a, b) => a.time - b.time);
        const uniqueBars: typeof formatted = [];
        const seenTimes = new Set<number>();
        for (const b of formatted) {
          if (!seenTimes.has(b.time)) {
            seenTimes.add(b.time);
            uniqueBars.push(b);
          }
        }

        // Load all candles from Data folder files to chart view
        this.raw1MinBars = uniqueBars;

        if (this.raw1MinBars.length > 0) {
          const last = this.raw1MinBars[this.raw1MinBars.length - 1];
          this.currentLiveBar = {
            time: last.time,
            open: last.open,
            high: last.high,
            low: last.low,
            close: last.close
          };
        }
        this.renderChartForTimeframe();
      },
      error: (err) => {
        console.warn('Error loading chart history for', symbol, err);
      }
    });
  }

  public loadHistoricalCandles(symbol: string) {
    this.isLoadingCandles = true;
    this.candlePage = 1;
    this.tradingService.getHistoricalCandles(symbol).subscribe({
      next: (data) => {
        this.historicalCandles = data || [];
        this.isLoadingCandles = false;

        // Sync LTP with the latest candle from historical data if not already receiving live prices
        if (this.historicalCandles.length > 0) {
          if (this.raw1MinBars.length === 0) {
            this.raw1MinBars = this.historicalCandles.map(c => ({
              time: c.timestamp,
              open: c.open,
              high: c.high,
              low: c.low,
              close: c.close
            }));
            this.renderChartForTimeframe();
          }

          const latestCandle = this.historicalCandles[this.historicalCandles.length - 1];
          const prevCandle = this.historicalCandles.length > 1 ? this.historicalCandles[this.historicalCandles.length - 2] : latestCandle;

          if (this.activeTick && (!this.activeTick.price || this.activeTick.price === 0)) {
            this.activeTick.price = latestCandle.close;
            this.activeTick.high = Math.max(this.activeTick.high || 0, latestCandle.high);
            this.activeTick.low = this.activeTick.low > 0 ? Math.min(this.activeTick.low, latestCandle.low) : latestCandle.low;
            this.activeTick.prevClose = prevCandle.close;
            this.activeTick.change = Number((this.activeTick.price - this.activeTick.prevClose).toFixed(2));
            this.activeTick.changePercent = this.activeTick.prevClose > 0 
              ? Number(((this.activeTick.change / this.activeTick.prevClose) * 100).toFixed(2)) 
              : 0;
            this.activeTick.timestamp = latestCandle.datetime.split(' ')[1] || this.currentMarketTime;
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
          this.filterStocks();
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

    // Use candleTime from tick or round to minute
    let candleTime = tick.candleTime && tick.candleTime > 0 
      ? tick.candleTime 
      : Math.floor(Date.now() / 60000) * 60;

    // Ensure strictly non-decreasing time for TradingView
    if (this.currentLiveBar && candleTime < this.currentLiveBar.time) {
      candleTime = this.currentLiveBar.time;
    }

    const candleOpen = tick.candleOpen && tick.candleOpen > 0 ? tick.candleOpen : tick.price;
    const candleHigh = tick.candleHigh && tick.candleHigh > 0 ? tick.candleHigh : tick.price;
    const candleLow = tick.candleLow && tick.candleLow > 0 ? tick.candleLow : tick.price;
    const candleClose = tick.candleClose && tick.candleClose > 0 ? tick.candleClose : tick.price;

    if (!this.currentLiveBar || this.currentLiveBar.time !== candleTime) {
      // If one or more intermediate minutes were skipped during live market, fill with flat continuity so 0 gaps exist
      if (this.currentLiveBar && candleTime > this.currentLiveBar.time + 60) {
        let fillTime = this.currentLiveBar.time + 60;
        const prevClose = this.currentLiveBar.close;
        while (fillTime < candleTime) {
          const gapBar = {
            time: fillTime,
            open: prevClose,
            high: prevClose,
            low: prevClose,
            close: prevClose
          };
          this.raw1MinBars.push(gapBar);
          if (this.selectedTimeframe === '1m' && this.candlestickSeries) {
            try { this.candlestickSeries.update(gapBar as any); } catch {}
          }
          fillTime += 60;
        }
      }

      // New 1-minute candle starts
      this.currentLiveBar = {
        time: candleTime,
        open: candleOpen,
        high: Math.max(candleOpen, candleHigh, candleClose),
        low: Math.min(candleOpen, candleLow, candleClose),
        close: candleClose
      };
      this.raw1MinBars.push({ ...this.currentLiveBar });
    } else {
      // Intra-minute update of current forming candle
      this.currentLiveBar.high = Math.max(this.currentLiveBar.high, candleHigh, candleClose);
      this.currentLiveBar.low = Math.min(this.currentLiveBar.low, candleLow, candleClose);
      this.currentLiveBar.close = candleClose;

      if (this.raw1MinBars.length > 0) {
        const lastRaw = this.raw1MinBars[this.raw1MinBars.length - 1];
        if (lastRaw.time === candleTime) {
          lastRaw.high = this.currentLiveBar.high;
          lastRaw.low = this.currentLiveBar.low;
          lastRaw.close = this.currentLiveBar.close;
        } else {
          this.raw1MinBars.push({ ...this.currentLiveBar });
        }
      } else {
        this.raw1MinBars.push({ ...this.currentLiveBar });
      }
    }

    // Update TradingView candlestick series smoothly based on active timeframe
    try {
      if (this.selectedTimeframe === '1m') {
        this.candlestickSeries.update({
          time: this.currentLiveBar.time as any,
          open: this.currentLiveBar.open,
          high: this.currentLiveBar.high,
          low: this.currentLiveBar.low,
          close: this.currentLiveBar.close
        });
        this.timeframeLiveBar = { ...this.currentLiveBar };
      } else {
        const bucketSeconds = this.getTimeframeSeconds(this.selectedTimeframe);
        const istOffset = 19800;
        let bucketTime: number;

        if (this.isSecondsTimeframe) {
          const currentSec = Math.floor(Date.now() / 1000);
          bucketTime = Math.floor(currentSec / bucketSeconds) * bucketSeconds;
        } else {
          bucketTime = Math.floor((this.currentLiveBar.time + istOffset) / bucketSeconds) * bucketSeconds - istOffset;
        }
        
        const bucketBars = this.raw1MinBars.filter(b => b.time >= bucketTime && b.time < bucketTime + bucketSeconds);
        const bOpen = bucketBars.length > 0 ? bucketBars[0].open : this.currentLiveBar.open;
        const bHigh = bucketBars.length > 0 ? Math.max(...bucketBars.map(b => b.high), this.currentLiveBar.high) : this.currentLiveBar.high;
        const bLow = bucketBars.length > 0 ? Math.min(...bucketBars.map(b => b.low), this.currentLiveBar.low) : this.currentLiveBar.low;
        const bClose = this.currentLiveBar.close;

        this.timeframeLiveBar = {
          time: bucketTime,
          open: bOpen,
          high: bHigh,
          low: bLow,
          close: bClose
        };

        this.candlestickSeries.update({
          time: bucketTime as any,
          open: bOpen,
          high: bHigh,
          low: bLow,
          close: bClose
        });
      }
    } catch (e) {
      console.warn('Candle chart update notice:', e);
    }

    // Continuously update the Live Candles Table in real time
    if (!this.historicalCandles) {
      this.historicalCandles = [];
    }

    const d = new Date(this.currentLiveBar.time * 1000);
    const istOffsetMs = 5.5 * 3600000;
    const istDate = new Date(d.getTime() + istOffsetMs);
    const dtStr = istDate.toISOString().replace('T', ' ').substring(0, 19);

    if (this.historicalCandles.length === 0) {
      this.historicalCandles = [{
        timestamp: this.currentLiveBar.time,
        datetime: dtStr,
        open: this.currentLiveBar.open,
        high: this.currentLiveBar.high,
        low: this.currentLiveBar.low,
        close: this.currentLiveBar.close,
        volume: tick.candleVolume || 1
      }];
    } else {
      const lastCandle = this.historicalCandles[this.historicalCandles.length - 1];
      if (lastCandle && lastCandle.timestamp === this.currentLiveBar.time) {
        lastCandle.high = Math.max(lastCandle.high, this.currentLiveBar.high);
        lastCandle.low = Math.min(lastCandle.low, this.currentLiveBar.low);
        lastCandle.close = this.currentLiveBar.close;
        lastCandle.volume = (tick.candleVolume && tick.candleVolume > 0) ? tick.candleVolume : lastCandle.volume + 1;
        this.historicalCandles = [...this.historicalCandles];
      } else if (lastCandle && this.currentLiveBar.time > lastCandle.timestamp) {
        this.historicalCandles = [
          ...this.historicalCandles,
          {
            timestamp: this.currentLiveBar.time,
            datetime: dtStr,
            open: this.currentLiveBar.open,
            high: this.currentLiveBar.high,
            low: this.currentLiveBar.low,
            close: this.currentLiveBar.close,
            volume: tick.candleVolume || 1
          }
        ];
      }
    }
  }

  // Modal Actions
  public openModal(type: 'BUY' | 'SELL') {
    this.modalOrderType = type;
    const currentPrice = this.activeTick?.price || 0;
    if (type === 'BUY') {
      this.modalStopLossPrice = currentPrice > 0 ? Number((currentPrice * 0.985).toFixed(2)) : null; // 1.5% below LTP
      this.modalTargetPrice = currentPrice > 0 ? Number((currentPrice * 1.03).toFixed(2)) : null;   // 3% above LTP
    } else {
      this.modalStopLossPrice = currentPrice > 0 ? Number((currentPrice * 1.015).toFixed(2)) : null;
      this.modalTargetPrice = currentPrice > 0 ? Number((currentPrice * 0.97).toFixed(2)) : null;
    }
    this.showOrderModal = true;
  }

  public closeModal() {
    this.showOrderModal = false;
  }

  public confirmOrder() {
    if (!this.activeTick || this.isSubmitting) return;

    this.isSubmitting = true;
    const price = this.activeTick.price;
    const finalSl = this.modalStopLossPrice ?? (this.modalOrderType === 'BUY'
      ? Number((price * 0.985).toFixed(2))
      : Number((price * 1.015).toFixed(2)));
    const finalTp = this.modalTargetPrice ?? (this.modalOrderType === 'BUY'
      ? Number((price * 1.03).toFixed(2))
      : Number((price * 0.97).toFixed(2)));

    this.tradingService.placeParentOrder(
      'P001', // Parent Account (Chandana)
      this.selectedSymbol,
      this.modalOrderType,
      price,
      this.modalQuantity,
      finalSl,
      finalTp
    ).subscribe({
      next: (res) => {
        this.isSubmitting = false;
        this.closeModal();
        this.loadAccounts(); // Updates parent and child balances
        this.handleNewOrderExecuted(res); // Replaces previous displayed order with new latest order
        this.loadOrders();   // Refreshes orders from DB to ensure persistence sync

        // Check for insufficient funds in parent or children
        if (res.parentOrder.orderStatus.includes('REJECTED')) {
          this.showToast(`⚠️ Order Not Placed: Parent Account has insufficient funds! ₹0 deducted.`);
        } else {
          const rejectedChildren = res.childOrders.filter(c => c.orderStatus.includes('INSUFFICIENT'));
          if (rejectedChildren.length > 0) {
            const names = rejectedChildren.map(c => this.cleanAccountName(c.childAccountName)).join(', ');
            this.showToast(`⚠️ Order Placed! Notice: ${names} skipped due to INSUFFICIENT FUNDS.`);
          } else {
            this.showToast(`✅ ${this.modalOrderType} Order ${res.parentOrder.orderId} Placed & Replicated to all Child Accounts!`);
          }
        }
      },
      error: (err) => {
        this.isSubmitting = false;
        console.error('Order error:', err);
        const msg = err?.error?.message || err?.message || 'Failed to place order';
        this.showToast(`❌ ${msg}`);
      }
    });
  }

  // Add Funds Actions
  public openAddFundsModal(accountId: string = 'P001') {
    this.fundTargetAccountId = accountId;
    this.fundDepositAmount = 50000;
    this.showAddFundsModal = true;
  }

  public closeAddFundsModal() {
    this.showAddFundsModal = false;
  }

  public setFundPreset(amt: number) {
    this.fundDepositAmount = amt;
  }

  public submitAddFunds() {
    if (this.fundDepositAmount <= 0 || this.isSubmittingFunds) return;
    this.isSubmittingFunds = true;

    this.tradingService.addFunds(this.fundTargetAccountId, this.fundDepositAmount).subscribe({
      next: (res) => {
        this.isSubmittingFunds = false;
        this.closeAddFundsModal();
        this.loadAccounts();
        this.showToast(`✅ ${res?.message || 'Funds added successfully!'}`);
      },
      error: (err) => {
        this.isSubmittingFunds = false;
        this.showToast(`❌ Failed to add funds: ${err?.error?.message || 'Unknown error'}`);
      }
    });
  }

  public resetAllFunds() {
    if (this.isSubmittingFunds) return;
    this.isSubmittingFunds = true;
    this.tradingService.resetDefaultFunds().subscribe({
      next: () => {
        this.isSubmittingFunds = false;
        this.loadAccounts();
        this.showToast(`✅ All accounts reset to default funds!`);
      },
      error: (err) => {
        this.isSubmittingFunds = false;
        this.showToast(`❌ Failed to reset funds`);
      }
    });
  }

  // Quick scenario test helper: Set a child account to ₹0 balance
  public setChildZeroFunds(childId: string = 'C002') {
    this.tradingService.setBalance(childId, 0).subscribe({
      next: () => {
        this.loadAccounts();
        this.showToast(`⚠️ Scenario Set: ${childId} balance is now ₹0 (Insufficient Funds).`);
      },
      error: (err) => {
        this.showToast(`❌ Failed to set balance: ${err?.error?.message || ''}`);
      }
    });
  }

  private handleNewOrderExecuted(res: OrderExecutionResult) {
    if (!res || !res.parentOrder) return;

    // Display ONLY latest single order in Parent Client Account (replaces previous displayed order)
    this.parentOrders = [res.parentOrder];

    // Maintain complete history in allParentOrders
    const pExists = this.allParentOrders.some(o => o.orderId === res.parentOrder.orderId);
    if (!pExists) {
      this.allParentOrders = [res.parentOrder, ...this.allParentOrders];
      this.totalParentOrdersCount = this.allParentOrders.filter(o => !o.orderStatus.includes('REJECTED')).length;
    }

    // Display ONLY latest single order in each Child Client Account (replaces previous displayed order)
    if (res.childOrders && res.childOrders.length > 0) {
      res.childOrders.forEach(co => {
        if (this.childOrdersMap[co.childAccountId]) {
          this.childOrdersMap[co.childAccountId] = [co]; // Replaced by previous order!
          if (!co.orderStatus.includes('REJECTED')) {
            this.childOrdersDbCount[co.childAccountId] = (this.childOrdersDbCount[co.childAccountId] || 0) + 1;
          }
        }

        const cExists = this.allChildOrders.some(o => o.childOrderId === co.childOrderId);
        if (!cExists) {
          this.allChildOrders = [co, ...this.allChildOrders];
        }
      });
      // Only count non-rejected orders (e.g. 55 instead of 56 when 1 order is rejected)
      this.totalChildOrdersCount = this.allChildOrders.filter(o => !o.orderStatus.includes('REJECTED')).length;
    }
  }

  public showToast(msg: string) {
    this.toastMessage = msg;
    setTimeout(() => {
      this.toastMessage = null;
    }, 4500);
  }
}
