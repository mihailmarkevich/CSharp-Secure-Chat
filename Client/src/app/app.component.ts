import {
  Component,
  OnInit,
  OnDestroy,
  AfterViewInit,
  ViewChild,
  ElementRef
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import { ChatService } from './chat.service';
import { ChatMessage } from './models/chat-message.model';
import { BanInfo } from './models/ban-info.model';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './app.component.html',
  styleUrls: ['./app.component.css']
})
export class AppComponent implements OnInit, AfterViewInit, OnDestroy {
  title = 'Secure Chat';

  @ViewChild('messageContainer') messageContainer?: ElementRef<HTMLDivElement>;

  nameConfirmed = false;
  displayName = '';
  messageText = '';

  messages: ChatMessage[] = [];
  banInfo: BanInfo | null = null;
  connectionError: string | null = null;

  private subs: Subscription[] = [];

  constructor(private readonly chat: ChatService) {}

  ngOnInit(): void {
    this.subs.push(
      this.chat.messages$.subscribe(msgs => {
        this.messages = msgs;
        setTimeout(() => this.scrollToBottom(), 0);
      }),
      this.chat.banInfo$.subscribe(info => {
        this.banInfo = info;
      }),
      this.chat.connectionError$.subscribe(err => {
        this.connectionError = err;
      }),
      this.chat.currentName$.subscribe(name => {
        this.nameConfirmed = !!name;

        this.displayName = name;
      })
    );

    this.chat.connect().catch(() => {});
  }

  ngAfterViewInit(): void {
    this.scrollToBottom();
  }

  ngOnDestroy(): void {
    this.subs.forEach(s => s.unsubscribe());
    this.chat.disconnect().catch(() => {});
  }

  get isConnected(): boolean {
    return this.chat.isConnected;
  }

  async onSetName(): Promise<void> {
    const trimmed = this.displayName.trim();
    if (!trimmed) {
      return;
    }

    await this.chat.changeName(trimmed);
  }

  async onSendMessage(): Promise<void> {
    const trimmed = this.messageText.trim();
    if (!trimmed) {
      return;
    }

    await this.chat.sendMessage(trimmed);
    this.messageText = '';
  }

  onMessageKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      this.onSendMessage();
    }
  }

  isOwnMessage(msg: ChatMessage): boolean {
    return msg.userName === this.displayName;
  }

  private scrollToBottom(): void {
    const el = this.messageContainer?.nativeElement;
    if (!el) return;

    el.scrollTop = el.scrollHeight;
  }
}
