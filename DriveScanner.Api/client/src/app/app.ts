import { Component, OnInit, OnDestroy, NgZone, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import * as signalR from '@microsoft/signalr';

interface SearchRule {
  id: string;
  categoryName: string;
  searchType: 'Literal' | 'Wildcard' | 'Regex';
  patternValue: string;
  isSensitive: boolean;
}

interface ScanProgressUpdate {
  currentFile: string;
  filesScanned: number;
  totalFilesFound: number;
  categoryMatchCounts: { [key: string]: number };
  categoryUniqueCounts: { [key: string]: number };
  isCompleted: boolean;
  errorMessage: string | null;
}

interface ScanMatchEntry {
  ruleId: string;
  categoryName: string;
  filePath: string;
  fileName: string;
  rawLine: string;
  sanitizedValue: string;
  lineNumber: number;
}

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './app.html',
  styleUrl: './app.css'
})
export class App implements OnInit, OnDestroy {
  // Config state
  targetPath = 'C:\\';
  blacklistInput = 'node_modules, bin, .git, obj, .vs';
  extensionsInput = '.config, .xml, .json, .txt, .properties, .yml, .yaml';

  // Dynamic Rule Matrix
  searchRules: SearchRule[] = [
    {
      id: 'rule_1',
      categoryName: 'Drive References',
      searchType: 'Regex',
      patternValue: '\\b[c|f|i|z]:\\\\',
      isSensitive: false
    },
    {
      id: 'rule_2',
      categoryName: 'Oracle Connection Strings',
      searchType: 'Regex',
      patternValue: '(DESCRIPTION\\s*=\\s*\\(ADDRESS_LIST\\s*=|Data Source\\s*=\\s*Oracle|Provider\\s*=\\s*OraOLEDB|User ID\\s*=\\s*[^;]+;\\s*Password\\s*=\\s*[^;]+)',
      isSensitive: true
    },
    {
      id: 'rule_3',
      categoryName: 'SQL Connection Strings',
      searchType: 'Regex',
      patternValue: '(Server\\s*=\\s*[^;]+;\\s*Database\\s*=\\s*[^;]+;\\s*User ID\\s*=\\s*[^;]+;\\s*Password\\s*=\\s*[^;]+|Server\\s*=\\s*[^;]+;\\s*Initial Catalog\\s*=\\s*[^;]+;\\s*Integrated Security\\s*=\\s*[^;]+|data source\\s*=\\s*[^;]+;\\s*initial catalog\\s*=\\s*[^;]+)',
      isSensitive: true
    },
    {
      id: 'rule_4',
      categoryName: 'Remote Server References',
      searchType: 'Regex',
      patternValue: '(^ncl\\w+|[a-zA-Z0-9.-]+\\.(aon\\.net|aon\\.com|cmp\\.net|cmp\\.com))',
      isSensitive: false
    },
    {
      id: 'rule_5',
      categoryName: 'NAS Drives',
      searchType: 'Regex',
      patternValue: '^\\\\\\\\[^\\\\]+\\\\[^\\\\]+',
      isSensitive: false
    },
    {
      id: 'rule_6',
      categoryName: 'HTTP/HTTPS/WWW',
      searchType: 'Regex',
      patternValue: 'https?:\\/\\/(www\\.)?[-a-zA-Z0-9@:%._\\+~#=]{1,256}\\.[a-zA-Z0-9()]{1,6}\\b([-a-zA-Z0-9()@:%_\\+.~#?&\\/\\/=]*)',
      isSensitive: false
    },
    {
      id: 'rule_7',
      categoryName: 'Service Accounts',
      searchType: 'Regex',
      patternValue: '\\bsvc[a-zA-Z0-9_-]+\\b',
      isSensitive: false
    },
    {
      id: 'rule_8',
      categoryName: 'App Lifecycle Stamps',
      searchType: 'Regex',
      patternValue: '\\b(dev|qa|qc|uat|prod)\\b',
      isSensitive: false
    },
    {
      id: 'rule_9',
      categoryName: 'Passwords',
      searchType: 'Regex',
      patternValue: '\\b(password|pwd|pass|secret)\\s*[:=]\\s*[\'"]?[a-zA-Z0-9_@#$%^&*()!+-]+[\'"]?',
      isSensitive: true
    }
  ];

  // Execution state
  isScanning = signal(false);
  sessionId = '';
  progress = signal<ScanProgressUpdate>({
    currentFile: '',
    filesScanned: 0,
    totalFilesFound: 0,
    categoryMatchCounts: {},
    categoryUniqueCounts: {},
    isCompleted: false,
    errorMessage: null
  });

  sampleMatches = signal<ScanMatchEntry[]>([]);
  totalMatchesCount = signal(0);
  selectedCategoryFilter = '';
  pageSize = 25;
  pageOffset = 0;

  get currentPage(): number {
    return Math.floor(this.pageOffset / this.pageSize) + 1;
  }

  get totalPages(): number {
    return Math.ceil(this.totalMatchesCount() / this.pageSize) || 1;
  }

  // SignalR connection
  private hubConnection?: signalR.HubConnection;
  private backendUrl = 'http://localhost:5224'; // Matches dotnet launchSettings HTTP port

  constructor(private http: HttpClient, private ngZone: NgZone) {}

  ngOnInit() {
    this.sessionId = 'session_' + Math.random().toString(36).substr(2, 9);
    
    // Dynamically adjust API backend URL to current window host
    const host = window.location.hostname;
    const protocol = window.location.protocol;
    const apiPort = protocol === 'https:' ? '7235' : '5224';
    this.backendUrl = `${protocol}//${host}:${apiPort}`;
    
    this.initSignalR();
  }

  ngOnDestroy() {
    this.hubConnection?.stop();
  }

  private initSignalR() {
    this.hubConnection = new signalR.HubConnectionBuilder()
      .withUrl(`${this.backendUrl}/hubs/scan`)
      .withAutomaticReconnect()
      .build();

    this.hubConnection
      .start()
      .then(() => {
        console.log('SignalR connection established');
        this.hubConnection?.send('JoinScanSession', this.sessionId);
      })
      .catch(err => console.error('Error starting SignalR: ', err));

    this.hubConnection.on('ReceiveProgress', (data: ScanProgressUpdate) => {
      this.ngZone.run(() => {
        this.progress.set(data);
        if (data.isCompleted) {
          this.isScanning.set(false);
          console.log('Scan completed successfully!', data);
          this.fetchSampleResults();
        }
      });
    });
  }

  startScan() {
    this.isScanning.set(true);
    this.sampleMatches.set([]);
    this.totalMatchesCount.set(0);
    this.pageOffset = 0;
    this.selectedCategoryFilter = '';

    // Reset progress UI display
    this.progress.set({
      currentFile: 'Starting scan...',
      filesScanned: 0,
      totalFilesFound: 0,
      categoryMatchCounts: {},
      categoryUniqueCounts: {},
      isCompleted: false,
      errorMessage: null
    });

    // Parse blacklist and extensions
    const blacklist = this.blacklistInput
      .split(',')
      .map(s => s.trim())
      .filter(s => s.length > 0);

    const extensions = this.extensionsInput
      .split(',')
      .map(s => s.trim())
      .filter(s => s.length > 0);

    const payload = {
      targetPath: this.targetPath,
      blacklist: blacklist,
      extensions: extensions,
      searchRules: this.searchRules
    };

    this.http.post(`${this.backendUrl}/api/scan?sessionId=${this.sessionId}`, payload)
      .subscribe({
        next: () => console.log('Scan command sent successfully'),
        error: (err) => {
          this.isScanning.set(false);
          const current = this.progress();
          current.errorMessage = 'Failed to trigger scan command: ' + (err.error || err.message);
          this.progress.set({ ...current });
        }
      });
  }

  cancelScan() {
    this.http.post(`${this.backendUrl}/api/scan/cancel`, {})
      .subscribe({
        next: () => {
          this.isScanning.set(false);
          const current = this.progress();
          current.currentFile = 'Scan cancelled by user.';
          this.progress.set({ ...current });
        },
        error: (err) => console.error('Cancellation failed: ', err)
      });
  }

  fetchSampleResults() {
    const url = `${this.backendUrl}/api/scan/results?offset=${this.pageOffset}&limit=${this.pageSize}&category=${encodeURIComponent(this.selectedCategoryFilter)}`;
    this.http.get<{ totalCount: number, sample: ScanMatchEntry[] }>(url)
      .subscribe({
        next: (res) => {
          this.sampleMatches.set(res.sample);
          this.totalMatchesCount.set(res.totalCount);
        },
        error: (err) => console.error('Error fetching samples: ', err)
      });
  }

  nextPage() {
    if (this.pageOffset + this.pageSize < this.totalMatchesCount()) {
      this.pageOffset += this.pageSize;
      this.fetchSampleResults();
    }
  }

  prevPage() {
    if (this.pageOffset - this.pageSize >= 0) {
      this.pageOffset -= this.pageSize;
      this.fetchSampleResults();
    }
  }

  selectFilter(category: string) {
    this.selectedCategoryFilter = category;
    this.pageOffset = 0;
    this.fetchSampleResults();
  }

  selectPageSize(size: any) {
    this.pageSize = Number(size);
    this.pageOffset = 0;
    this.fetchSampleResults();
  }

  isExporting = signal(false);

  downloadReport() {
    this.isExporting.set(true);
    this.http.get(`${this.backendUrl}/api/scan/export`, { responseType: 'blob' })
      .subscribe({
        next: (blob) => {
          this.isExporting.set(false);
          const url = window.URL.createObjectURL(blob);
          const a = document.createElement('a');
          a.href = url;
          a.download = `ScanReport_${new Date().toISOString().replace(/[-:T]/g, '_').slice(0, 15)}.xlsx`;
          document.body.appendChild(a);
          a.click();
          document.body.removeChild(a);
          window.URL.revokeObjectURL(url);
        },
        error: (err) => {
          this.isExporting.set(false);
          console.error('Error generating report: ', err);
          alert('Failed to generate excel report: ' + err.message);
        }
      });
  }

  addCustomRule() {
    const nextId = 'rule_' + (this.searchRules.length + 1);
    this.searchRules.push({
      id: nextId,
      categoryName: 'Custom Keyword',
      searchType: 'Literal',
      patternValue: 'New Keyword',
      isSensitive: false
    });
  }

  removeRule(index: number) {
    this.searchRules.splice(index, 1);
  }
}
