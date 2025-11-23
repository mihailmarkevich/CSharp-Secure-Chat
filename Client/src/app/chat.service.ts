import { Injectable } from '@angular/core';
import { BehaviorSubject, Observable } from 'rxjs';
import * as signalR from '@microsoft/signalr';
import { environment } from '../environments/environment';
import { ChatMessage } from './models/chat-message.model';
import { BanInfo } from './models/ban-info.model';

@Injectable({ providedIn: 'root' })
export class ChatService {
  private hubConnection?: signalR.HubConnection;

  private myConnectionIdSubject = new BehaviorSubject<string | null>(null);
  readonly myConnectionId$ = this.myConnectionIdSubject.asObservable();

  private messagesSubject = new BehaviorSubject<ChatMessage[]>([]);
  readonly messages$: Observable<ChatMessage[]> = this.messagesSubject.asObservable();

  private banInfoSubject = new BehaviorSubject<BanInfo | null>(null);
  readonly banInfo$: Observable<BanInfo | null> = this.banInfoSubject.asObservable();

  private connectionErrorSubject = new BehaviorSubject<string | null>(null);
  readonly connectionError$: Observable<string | null> = this.connectionErrorSubject.asObservable();

  private currentNameSubject = new BehaviorSubject<string>('');
  readonly currentName$: Observable<string> = this.currentNameSubject.asObservable();

  constructor() {
    this.buildConnection();
  }

  private buildConnection(): void {
    this.hubConnection = new signalR.HubConnectionBuilder()
      .withUrl(environment.chatHubUrl, {
        withCredentials: false
      })
      .withAutomaticReconnect()
      .build();

    this.registerHandlers();
  }

  private registerHandlers(): void {
    if (!this.hubConnection) {
      return;
    }

    this.hubConnection.on('ReceiveMessage', (msg: ChatMessage) => {
      const current = this.messagesSubject.value;
      this.messagesSubject.next([...current, msg]);
    });

    this.hubConnection.on('UserNameChanged', (connectionId: string, newName: string) => {
      const updated = this.messagesSubject.value.map(m =>
        m.connectionId === connectionId ? { ...m, userName: newName } : m
      );
      this.messagesSubject.next(updated);
    });

    this.hubConnection.on('Banned', (payload: BanInfo) => {
      this.handleBan(payload);
    });
  }

  // helpers

  private extractHubErrorMessage(error: any): string {
    if (!error) {
      return 'Unexpected error.';
    }

    let raw = '';

    if (typeof error === 'string') {
      raw = error;
    } else if (error.message && typeof error.message === 'string') {
      raw = error.message;
    } else {
      try {
        raw = JSON.stringify(error);
      } catch {
        raw = String(error);
      }
    }

    const marker = 'HubException:';
    const idx = raw.indexOf(marker);
    if (idx >= 0) {
      return raw.substring(idx + marker.length).trim();
    }

    return raw || 'Unexpected error.';
  }

  private handleHubError(error: any): void {
    const msg = this.extractHubErrorMessage(error);
    const lower = msg.toLowerCase();

    if (lower.includes('temporarily') && (lower.includes('blocked') || lower.includes('banned'))) {
      const banInfo: BanInfo = {
        message: msg
      };

      this.handleBan(banInfo);
    } else {
      this.connectionErrorSubject.next(msg);
    }
  }

  private handleBan(payload: BanInfo): void {
    this.banInfoSubject.next(payload);
    this.connectionErrorSubject.next(null);

    this.hubConnection?.stop().catch(() => {});
  }

  // public methods

  async connect(): Promise<void> {
    if (!this.hubConnection) {
      this.buildConnection();
    }

    if (this.hubConnection?.state === signalR.HubConnectionState.Connected) {
      return;
    }

    this.connectionErrorSubject.next(null);
    this.banInfoSubject.next(null);

    try {
      await this.hubConnection!.start();

      this.myConnectionIdSubject.next(this.hubConnection!.connectionId ?? null);

      await this.loadHistory();

      const savedName = this.currentNameSubject.value?.trim();
      if (savedName) {
        await this.changeName(savedName);
      }
    } catch (error: any) {
      const statusCode =
        error && typeof error === 'object' && 'statusCode' in error
          ? (error as any).statusCode
          : undefined;

      const message: string = error?.message ?? '';

      if (statusCode === 403 || message.includes('403')) {
        this.connectionErrorSubject.next(
          'You are temporarily blocked by the server. Please wait a few seconds and try again.'
        );
      } else {
        this.connectionErrorSubject.next(
          'Failed to connect to the chat server. Please try again later.'
        );
      }
    }
  }

  async disconnect(): Promise<void> {
    if (this.hubConnection && this.hubConnection.state === signalR.HubConnectionState.Connected) {
      await this.hubConnection.stop();
    }
  }

  private async loadHistory(count: number = 50): Promise<void> {
    if (!this.hubConnection || this.hubConnection.state !== signalR.HubConnectionState.Connected) {
      return;
    }

    try {
      const history = await this.hubConnection.invoke<ChatMessage[]>('GetHistory', count);
      const sorted = [...history].sort(
        (a, b) => new Date(a.timestamp).getTime() - new Date(b.timestamp).getTime()
      );
      this.messagesSubject.next(sorted);
    } catch {
      //
    }
  }

  async sendMessage(text: string): Promise<void> {
    if (!this.hubConnection || this.hubConnection.state !== signalR.HubConnectionState.Connected) {
      this.connectionErrorSubject.next('Not connected to server.');
      return;
    }

    const trimmed = text.trim();
    if (!trimmed) {
      return;
    }

    try {
      await this.hubConnection.invoke('SendMessage', trimmed);
    } catch (error: any) {
      this.handleHubError(error);
    }
  }

  async changeName(name: string): Promise<void> {
    if (!this.hubConnection || this.hubConnection.state !== signalR.HubConnectionState.Connected) {
      this.connectionErrorSubject.next('Not connected to server.');
      return;
    }

    const trimmed = name.trim();
    if (!trimmed) {
      return;
    }

    try {
      await this.hubConnection.invoke('ChangeName', trimmed);
      this.currentNameSubject.next(trimmed);
      this.connectionErrorSubject.next(null);
    } catch (error: any) {
      this.handleHubError(error);
    }
  }

  get isConnected(): boolean {
    return this.hubConnection?.state === signalR.HubConnectionState.Connected;
  }
}
