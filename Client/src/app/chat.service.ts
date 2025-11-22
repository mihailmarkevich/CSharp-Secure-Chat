import { Injectable } from '@angular/core';
import { BehaviorSubject, Observable } from 'rxjs';
import * as signalR from '@microsoft/signalr';
import { environment } from '../environments/environment';
import { ChatMessage } from './models/chat-message.model';
import { BanInfo } from './models/ban-info.model';

@Injectable({ providedIn: 'root' })
export class ChatService {
  private hubConnection?: signalR.HubConnection;

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

    this.hubConnection.on(
      'ReceiveMessage',
      (id: string, connectionId: string, userName: string, text: string, timestamp: string) => {
        const msg: ChatMessage = {
          id,
          connectionId,
          userName,
          text,
          timestamp
        };

        const current = this.messagesSubject.value;
        this.messagesSubject.next([...current, msg]);
      }
    );

    this.hubConnection.on('UserNameChanged', (connectionId: string, newName: string) => {
      const updated = this.messagesSubject.value.map(m =>
        m.connectionId === connectionId ? { ...m, userName: newName } : m
      );
      this.messagesSubject.next(updated);
    });

    this.hubConnection.on('Banned', (payload: BanInfo) => {
      this.banInfoSubject.next(payload);
      this.hubConnection?.stop().catch(() => {});
    });
  }

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
      await this.loadHistory();
    } catch (error: any) {
      const statusCode = (error && typeof error === 'object' && 'statusCode' in error)
        ? (error as any).statusCode
        : undefined;

      const message: string = error?.message ?? '';

      if (statusCode === 403 || message.includes('403')) {
        this.banInfoSubject.next({
          message: 'You are temporarily blocked or banned by the server.',
          retryAfterSeconds: undefined
        });
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

    await this.hubConnection.invoke('SendMessage', trimmed);
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

    await this.hubConnection.invoke('ChangeName', trimmed);
    this.currentNameSubject.next(trimmed);
  }

  get isConnected(): boolean {
    return this.hubConnection?.state === signalR.HubConnectionState.Connected;
  }
}
