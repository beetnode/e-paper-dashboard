import { Component, inject } from '@angular/core';
import { RouterModule } from '@angular/router';
import { AuthService } from '../../services/auth.service';

@Component({
  selector: 'app-home',
  imports: [RouterModule],
  template: `
    <div class="izboard-hero bg-transition-300">
      <div class="hero-content px-3">
        <!-- Logo -->
        <div class="mb-4 text-center">
          <img src="icon.svg" alt="" class="hero-logo">
        </div>

        <!-- Headline -->
        <h1 class="fw-bold hero-title mb-3 text-center">
          izBoard
        </h1>

        <!-- Subtitle -->
        <p class="hero-subtitle text-center mb-4">
          Dashboards for battery-powered e-paper displays, updated on your schedule
        </p>

        <!-- Core features -->
        <div class="hero-features mb-4">
          <div class="feature-item">
            <i class="fa-solid fa-palette" aria-hidden="true"></i>
            <div>Design custom layouts from widgets in a drag-and-drop designer</div>
          </div>
          <div class="feature-item">
            <i class="fa-solid fa-plug" aria-hidden="true"></i>
            <div>Feed widgets with data from Home Assistant and Google Calendar &amp; Tasks</div>
          </div>
          <div class="feature-item">
            <i class="fa-solid fa-house-signal" aria-hidden="true"></i>
            <div>Show existing Home Assistant dashboards with the optional rendering component</div>
          </div>
          <div class="feature-item">
            <i class="fa-solid fa-wand-magic-sparkles" aria-hidden="true"></i>
            <div>Generate widget content from a plain-language prompt with your own AI provider</div>
          </div>
          <div class="feature-item">
            <i class="fa-solid fa-clock" aria-hidden="true"></i>
            <div>Displays wake at the times you choose and sleep in between for long battery life</div>
          </div>
          <div class="feature-item">
            <i class="fa-solid fa-microchip" aria-hidden="true"></i>
            <div>Pair displays securely and update their firmware over the air</div>
          </div>
        </div>

        <!-- CTA Buttons -->
        <div class="d-flex gap-3 justify-content-center flex-wrap">
          <a class="btn btn-primary btn-lg px-5 hero-btn-primary" routerLink="/dashboards">
            {{ isAuthenticated() ? 'Open dashboards' : 'Get started' }}
          </a>
          <a class="btn btn-outline-primary btn-lg px-5 hero-btn-github" href="https://github.com/izdev-digital/e-paper-dashboard" target="_blank" rel="noopener noreferrer">
            GitHub
          </a>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .izboard-hero {
      position:relative;
      min-height:100%;
      overflow:clip;
      padding: clamp(1rem, 3.5vh, 2rem) 0 max(2rem, env(safe-area-inset-bottom));
      background: var(--bs-body-bg);
    }

    .hero-content {
      width: 100%;
      max-width: 960px;
      margin: 0 auto;
    }

    .hero-title {
      font-size: clamp(2.25rem, 7vw, 3rem);
      letter-spacing: -1px;
      line-height: 1.2;
    }

    .hero-subtitle {
      max-width: 520px;
      margin-inline: auto;
      color: var(--bs-secondary-color);
      font-size: 1.15rem;
      line-height: 1.45;
    }

    .hero-features {
      display: flex;
      flex-direction: column;
      gap: 0.5rem;
      max-width: 900px;
      margin-inline: auto;
    }

    .feature-item {
      display: flex;
      align-items: flex-start;
      gap: 0.75rem;
      padding: 0.75rem 0.875rem;
      border: 1px solid rgba(128,128,128,0.3);
      border-radius: 0.75rem;
      background: rgba(128,128,128,0.1);
      box-shadow: 0 2px 4px rgba(0,0,0,0.08);
      transition: all 0.2s ease;
    }

    .feature-item > i {
      flex-shrink: 0;
      margin-top: 0.2rem;
      color: var(--bs-primary);
      font-size: 1.15rem;
    }

    .feature-item > div {
      flex: 1;
      color: var(--bs-body-color);
      font-size: 1rem;
      line-height: 1.5;
    }

    
    /* Light mode - subtle neutral gradient */
    [data-bs-theme="light"] .izboard-hero {
      background: linear-gradient(135deg, #ffffff 0%, #f8f9fa 100%);
    }
    
    /* Dark mode - subtle dark gradient */
    [data-bs-theme="dark"] .izboard-hero {
      background: linear-gradient(135deg, #212529 0%, #1a1d20 100%);
    }
    
    .izboard-hero::before {
      content:'';
      position:absolute;
      top:0;
      left:0;
      right:0;
      bottom:0;
      pointer-events:none;
      opacity:0.4;
      transition: inherit;
    }
    
    /* Light mode - very subtle accent pattern */
    [data-bs-theme="light"] .izboard-hero::before {
      background-image:
        radial-gradient(circle at 20% 80%, rgba(108, 117, 125, 0.04) 0%, transparent 50%),
        radial-gradient(circle at 80% 20%, rgba(108, 117, 125, 0.04) 0%, transparent 50%);
    }
    
    /* Dark mode - slightly visible accent pattern */
    [data-bs-theme="dark"] .izboard-hero::before {
      background-image:
        radial-gradient(circle at 20% 80%, rgba(108, 117, 125, 0.08) 0%, transparent 50%),
        radial-gradient(circle at 80% 20%, rgba(108, 117, 125, 0.08) 0%, transparent 50%);
    }
    
    .izboard-hero > * {
      position:relative;
      z-index:1;
    }
    
    .feature-item:hover {
      transform: translateY(-1px);
      box-shadow: 0 4px 12px rgba(0, 0, 0, 0.12) !important;
      border-color: rgba(128, 128, 128, 0.4) !important;
      background: rgba(128, 128, 128, 0.14) !important;
    }
    
    .feature-item:active {
      transform: translateY(0);
      box-shadow: 0 2px 4px rgba(0, 0, 0, 0.08) !important;
    }
    
    .hero-title {
      color: var(--bs-body-color);
    }
    
    .hero-subtitle {
      color: var(--bs-body-color);
      font-weight: 500;
    }
    
    .hero-logo {
      width:120px;
      height:120px;
      border-radius:20px;
      transition: box-shadow 0.3s ease, transform 0.3s ease;
    }
    
    .hero-logo:hover {
      transform: scale(1.03);
    }
    
    [data-bs-theme="light"] .hero-logo {
      box-shadow: 0 16px 56px rgba(0, 0, 0, 0.12);
    }
    
    [data-bs-theme="dark"] .hero-logo {
      box-shadow: 0 16px 56px rgba(0, 0, 0, 0.5);
    }
    
    .hero-btn-primary,
    .hero-btn-github {
      transition: all 0.2s ease;
      padding: 0.75rem 2.25rem;
      border-width: 1.5px;
      font-size: 1.05rem;
      font-weight: 600;
    }
    
    .hero-btn-primary:hover {
      transform: translateY(-2px);
      box-shadow: 0 8px 24px rgba(0, 0, 0, 0.15);
    }
    
    .hero-btn-github:hover {
      transform: translateY(-2px);
    }
    
    .btn {
      transition:all 0.3s cubic-bezier(0.4,0,0.2,1);
      text-decoration:none !important;
    }
    
    .btn:hover {
      transform:translateY(-2px);
    }
    
    [data-bs-theme="light"] .btn-primary:hover {
      box-shadow:0 12px 24px rgba(13, 110, 253, 0.25);
    }
    
    [data-bs-theme="dark"] .btn-primary:hover {
      box-shadow:0 12px 24px rgba(13, 110, 253, 0.4);
    }
    
    [data-bs-theme="light"] .btn-outline-primary:hover {
      box-shadow:0 12px 24px rgba(13, 110, 253, 0.15);
    }
    
    [data-bs-theme="dark"] .btn-outline-primary:hover {
      box-shadow:0 12px 24px rgba(13, 110, 253, 0.3);
    }
    
    @media (max-width:768px) {
      .display-3 {
        font-size:2rem;
      }
      .hero-subtitle {
        font-size:1.1rem !important;
      }
      .btn-lg {
        font-size:1rem !important;
        padding: 0.5rem 1.5rem !important;
      }
      .hero-logo {
        width:100px;
        height:100px;
      }
      .izboard-hero {
        padding-top: 1rem;
      }
    }

    @media (min-width: 769px) {
      .hero-features {
        display: grid;
        grid-template-columns: repeat(2, minmax(0, 1fr));
      }
    }
  `]
})
export class HomeComponent {
  protected readonly isAuthenticated = inject(AuthService).isAuthenticated;
}
