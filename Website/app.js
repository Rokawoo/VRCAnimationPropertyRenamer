import { baseLayerLuminance, StandardLuminance } from 'https://unpkg.com/@fluentui/web-components';

// Application state
const state = {
  currentTab: 'renamer',
  isLoaded: false,
  observers: new Map(),
  animations: new Map()
};

// Utility functions
const utils = {
  // Debounce function for performance
  debounce(func, wait) {
    let timeout;
    return function executedFunction(...args) {
      const later = () => {
        clearTimeout(timeout);
        func(...args);
      };
      clearTimeout(timeout);
      timeout = setTimeout(later, wait);
    };
  },

  // Throttle function for scroll events
  throttle(func, limit) {
    let inThrottle;
    return function() {
      const args = arguments;
      const context = this;
      if (!inThrottle) {
        func.apply(context, args);
        inThrottle = true;
        setTimeout(() => inThrottle = false, limit);
      }
    }
  },

  // Get element with error handling
  getElement(selector) {
    const element = document.querySelector(selector);
    if (!element) {
      console.warn(`Element not found: ${selector}`);
    }
    return element;
  },

  // Get elements with error handling
  getElements(selector) {
    const elements = document.querySelectorAll(selector);
    if (elements.length === 0) {
      console.warn(`No elements found: ${selector}`);
    }
    return elements;
  },

  // Random number generator
  random(min, max) {
    return Math.random() * (max - min) + min;
  },

  // Easing functions
  easing: {
    easeOutCubic: t => 1 - Math.pow(1 - t, 3),
    easeInOutCubic: t => t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2,
    easeOutBounce: t => {
      const n1 = 7.5625;
      const d1 = 2.75;
      if (t < 1 / d1) {
        return n1 * t * t;
      } else if (t < 2 / d1) {
        return n1 * (t -= 1.5 / d1) * t + 0.75;
      } else if (t < 2.5 / d1) {
        return n1 * (t -= 2.25 / d1) * t + 0.9375;
      } else {
        return n1 * (t -= 2.625 / d1) * t + 0.984375;
      }
    }
  }
};

// Theme management
const themeManager = {
  init() {
    this.setTheme();
    this.watchThemeChanges();
  },

  setTheme() {
    const isDarkTheme = window.matchMedia("(prefers-color-scheme: dark)").matches;
    const luminance = isDarkTheme ? StandardLuminance.DarkMode : StandardLuminance.LightMode;
    baseLayerLuminance.setValueFor(document.documentElement, luminance);
    
    // Add theme class to body for additional styling
    document.body.classList.toggle('dark-theme', isDarkTheme);
    document.body.classList.toggle('light-theme', !isDarkTheme);
  },

  watchThemeChanges() {
    window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', () => {
      this.setTheme();
    });
  }
};

// Animation system
const animationSystem = {
  init() {
    this.setupIntersectionObserver();
    this.setupScrollAnimations();
    this.setupParallaxEffects();
    this.setupFloatingShapes();
  },

  setupIntersectionObserver() {
    const observerOptions = {
      threshold: 0.1,
      rootMargin: '0px 0px -50px 0px'
    };

    const observer = new IntersectionObserver((entries) => {
      entries.forEach((entry, index) => {
        if (entry.isIntersecting) {
          this.animateElement(entry.target, index);
        }
      });
    }, observerOptions);

    // Observe animated elements
    const animatedElements = utils.getElements(`
      .feature-card,
      .demo-card,
      .tool-card,
      .step,
      .install-method
    `);

    animatedElements.forEach((el, index) => {
      el.style.opacity = '0';
      el.style.transform = 'translateY(30px)';
      el.style.transition = 'opacity 0.8s cubic-bezier(0.4, 0, 0.2, 1), transform 0.8s cubic-bezier(0.4, 0, 0.2, 1)';
      observer.observe(el);
    });

    state.observers.set('intersection', observer);
  },

  animateElement(element, index) {
    const delay = index * 100;
    setTimeout(() => {
      element.style.opacity = '1';
      element.style.transform = 'translateY(0)';
    }, delay);
  },

  setupScrollAnimations() {
    const handleScroll = utils.throttle(() => {
      const scrolled = window.pageYOffset;
      const rate = scrolled * -0.5;
      
      // Parallax effect on hero shapes
      const shapes = utils.getElements('.shape');
      shapes.forEach((shape, index) => {
        const speed = 0.5 + (index * 0.1);
        shape.style.transform = `translateY(${scrolled * speed}px) rotate(${scrolled * 0.1}deg)`;
      });

      // Hero content parallax
      const heroContent = utils.getElement('.hero-content');
      if (heroContent && scrolled < window.innerHeight) {
        heroContent.style.transform = `translateY(${scrolled * 0.3}px)`;
      }

      // Update scroll indicator
      const scrollIndicator = utils.getElement('.scroll-indicator');
      if (scrollIndicator) {
        const opacity = Math.max(0, 1 - (scrolled / window.innerHeight));
        scrollIndicator.style.opacity = opacity;
      }
    }, 16);

    window.addEventListener('scroll', handleScroll, { passive: true });
  },

  setupParallaxEffects() {
    const parallaxElements = utils.getElements('[data-parallax]');
    
    const handleParallax = utils.throttle(() => {
      const scrolled = window.pageYOffset;
      
      parallaxElements.forEach(element => {
        const speed = element.dataset.parallax || 0.5;
        const yPos = -(scrolled * speed);
        element.style.transform = `translateY(${yPos}px)`;
      });
    }, 16);

    if (parallaxElements.length > 0) {
      window.addEventListener('scroll', handleParallax, { passive: true });
    }
  },

  setupFloatingShapes() {
    const shapes = utils.getElements('.shape');
    
    shapes.forEach((shape, index) => {
      // Add random delay and duration variations
      const duration = utils.random(20, 40);
      const delay = utils.random(0, 5);
      
      shape.style.animationDuration = `${duration}s`;
      shape.style.animationDelay = `-${delay}s`;
      
      // Add random size variations
      const scale = utils.random(0.8, 1.2);
      shape.style.transform = `scale(${scale})`;
    });
  },

  // Custom animation for elements
  animateIn(element, animation = 'fadeInUp', delay = 0) {
    return new Promise((resolve) => {
      element.style.opacity = '0';
      element.style.transform = this.getInitialTransform(animation);
      
      setTimeout(() => {
        element.style.transition = 'all 0.8s cubic-bezier(0.4, 0, 0.2, 1)';
        element.style.opacity = '1';
        element.style.transform = 'none';
        
        setTimeout(resolve, 800);
      }, delay);
    });
  },

  getInitialTransform(animation) {
    const transforms = {
      fadeInUp: 'translateY(30px)',
      fadeInLeft: 'translateX(-30px)',
      fadeInRight: 'translateX(30px)',
      fadeInScale: 'scale(0.9)',
      fadeInRotate: 'rotate(5deg) scale(0.95)'
    };
    return transforms[animation] || transforms.fadeInUp;
  }
};

// Interactive elements
const interactiveElements = {
  init() {
    this.setupButtons();
    this.setupCards();
    this.setupVideos();
    this.setupTabs();
    this.setupSmoothScrolling();
  },

  setupButtons() {
    // Add ripple effect to buttons
    const buttons = utils.getElements('.cta-button, .install-button, .tab-button');
    
    buttons.forEach(button => {
      button.addEventListener('click', this.createRipple.bind(this));
      
      // Add hover sound effect (if audio is enabled)
      button.addEventListener('mouseenter', () => {
        button.style.transform = 'translateY(-2px)';
      });
      
      button.addEventListener('mouseleave', () => {
        button.style.transform = '';
      });
    });

    // Special effects for primary CTA
    const primaryCta = utils.getElement('.cta-button.primary');
    if (primaryCta) {
      this.setupPrimaryCTA(primaryCta);
    }
  },

  createRipple(event) {
    const button = event.currentTarget;
    const rect = button.getBoundingClientRect();
    const size = Math.max(rect.width, rect.height);
    const x = event.clientX - rect.left - size / 2;
    const y = event.clientY - rect.top - size / 2;
    
    const ripple = document.createElement('div');
    ripple.className = 'ripple';
    ripple.style.cssText = `
      position: absolute;
      width: ${size}px;
      height: ${size}px;
      left: ${x}px;
      top: ${y}px;
      background: radial-gradient(circle, rgba(255,255,255,0.3) 0%, transparent 70%);
      border-radius: 50%;
      pointer-events: none;
      transform: scale(0);
      animation: ripple 0.6s ease-out;
    `;
    
    // Add ripple animation
    if (!document.head.querySelector('#ripple-styles')) {
      const style = document.createElement('style');
      style.id = 'ripple-styles';
      style.textContent = `
        @keyframes ripple {
          to {
            transform: scale(2);
            opacity: 0;
          }
        }
      `;
      document.head.appendChild(style);
    }
    
    button.style.position = 'relative';
    button.style.overflow = 'hidden';
    button.appendChild(ripple);
    
    setTimeout(() => {
      ripple.remove();
    }, 600);
  },

  setupPrimaryCTA(button) {
    // Add particle effect on hover
    button.addEventListener('mouseenter', () => {
      this.createParticles(button);
    });
  },

  createParticles(element) {
    const rect = element.getBoundingClientRect();
    const particleCount = 12;
    
    for (let i = 0; i < particleCount; i++) {
      const particle = document.createElement('div');
      particle.className = 'particle';
      particle.style.cssText = `
        position: fixed;
        width: 4px;
        height: 4px;
        background: var(--primary-light);
        border-radius: 50%;
        pointer-events: none;
        z-index: 1000;
        left: ${rect.left + rect.width / 2}px;
        top: ${rect.top + rect.height / 2}px;
      `;
      
      document.body.appendChild(particle);
      
      // Animate particle
      const angle = (i / particleCount) * Math.PI * 2;
      const velocity = utils.random(50, 100);
      const life = utils.random(800, 1200);
      
      particle.animate([
        {
          transform: 'translate(0, 0) scale(1)',
          opacity: 1
        },
        {
          transform: `translate(${Math.cos(angle) * velocity}px, ${Math.sin(angle) * velocity}px) scale(0)`,
          opacity: 0
        }
      ], {
        duration: life,
        easing: 'cubic-bezier(0.25, 0.46, 0.45, 0.94)'
      }).onfinish = () => particle.remove();
    }
  },

  setupCards() {
    const cards = utils.getElements('.feature-card, .demo-card, .tool-card');
    
    cards.forEach(card => {
      // Add tilt effect
      card.addEventListener('mousemove', (e) => {
        const rect = card.getBoundingClientRect();
        const x = e.clientX - rect.left;
        const y = e.clientY - rect.top;
        
        const centerX = rect.width / 2;
        const centerY = rect.height / 2;
        
        const rotateX = (y - centerY) / 10;
        const rotateY = (centerX - x) / 10;
        
        card.style.transform = `perspective(1000px) rotateX(${rotateX}deg) rotateY(${rotateY}deg) translateZ(10px)`;
      });
      
      card.addEventListener('mouseleave', () => {
        card.style.transform = '';
      });
      
      // Add glowing border effect
      card.addEventListener('mouseenter', () => {
        this.addGlowEffect(card);
      });
    });
  },

  addGlowEffect(element) {
    const glow = document.createElement('div');
    glow.className = 'glow-effect';
    glow.style.cssText = `
      position: absolute;
      inset: -2px;
      background: var(--gradient-primary);
      border-radius: inherit;
      z-index: -1;
      opacity: 0;
      filter: blur(8px);
      transition: opacity 0.3s ease;
    `;
    
    element.style.position = 'relative';
    element.appendChild(glow);
    
    // Animate glow
    requestAnimationFrame(() => {
      glow.style.opacity = '0.3';
    });
    
    // Remove glow on mouse leave
    const removeGlow = () => {
      glow.style.opacity = '0';
      setTimeout(() => glow.remove(), 300);
      element.removeEventListener('mouseleave', removeGlow);
    };
    
    element.addEventListener('mouseleave', removeGlow);
  },

  setupVideos() {
    const videoContainers = utils.getElements('.demo-video-container');
    
    videoContainers.forEach(container => {
      const video = container.querySelector('video');
      const overlay = container.querySelector('.video-overlay');
      const playButton = container.querySelector('.play-button');
      
      if (video && overlay && playButton) {
        // Play/pause functionality
        const togglePlay = () => {
          if (video.paused) {
            video.play();
            overlay.style.opacity = '0';
          } else {
            video.pause();
            overlay.style.opacity = '1';
          }
        };
        
        playButton.addEventListener('click', togglePlay);
        video.addEventListener('click', togglePlay);
        
        // Show controls when video is playing
        video.addEventListener('play', () => {
          video.controls = true;
          overlay.style.opacity = '0';
        });
        
        video.addEventListener('pause', () => {
          overlay.style.opacity = '1';
        });
        
        // Hide controls when mouse leaves and video is paused
        container.addEventListener('mouseleave', () => {
          if (video.paused) {
            video.controls = false;
          }
        });
      }
    });
  },

  setupTabs() {
    const tabButtons = utils.getElements('.tab-button');
    const tabContents = utils.getElements('.tab-content');
    
    tabButtons.forEach(button => {
      button.addEventListener('click', () => {
        const targetTab = button.dataset.tab;
        this.switchTab(targetTab);
      });
    });
  },

  switchTab(targetTab) {
    if (state.currentTab === targetTab) return;
    
    // Update buttons
    utils.getElements('.tab-button').forEach(button => {
      button.classList.toggle('active', button.dataset.tab === targetTab);
    });
    
    // Update content with animation
    const currentContent = utils.getElement(`#${state.currentTab}`);
    const targetContent = utils.getElement(`#${targetTab}`);
    
    if (currentContent && targetContent) {
      // Fade out current
      currentContent.style.opacity = '0';
      currentContent.style.transform = 'translateY(20px)';
      
      setTimeout(() => {
        currentContent.classList.remove('active');
        targetContent.classList.add('active');
        
        // Fade in new
        requestAnimationFrame(() => {
          targetContent.style.opacity = '1';
          targetContent.style.transform = 'translateY(0)';
        });
      }, 300);
    }
    
    state.currentTab = targetTab;
  },

  setupSmoothScrolling() {
    // Smooth scroll for anchor links
    const anchorLinks = utils.getElements('a[href^="#"]');
    
    anchorLinks.forEach(link => {
      link.addEventListener('click', (e) => {
        const href = link.getAttribute('href');
        if (href === '#') return;
        
        const target = utils.getElement(href);
        if (target) {
          e.preventDefault();
          this.smoothScrollTo(target);
        }
      });
    });
  },

  smoothScrollTo(target, duration = 1000) {
    const targetPosition = target.getBoundingClientRect().top + window.pageYOffset - 100;
    const startPosition = window.pageYOffset;
    const distance = targetPosition - startPosition;
    let startTime = null;
    
    const animation = (currentTime) => {
      if (startTime === null) startTime = currentTime;
      const timeElapsed = currentTime - startTime;
      const progress = Math.min(timeElapsed / duration, 1);
      
      const ease = utils.easing.easeInOutCubic(progress);
      window.scrollTo(0, startPosition + distance * ease);
      
      if (progress < 1) {
        requestAnimationFrame(animation);
      }
    };
    
    requestAnimationFrame(animation);
  }
};

// Performance monitoring
const performance = {
  init() {
    this.monitorFPS();
    this.optimizeAnimations();
  },

  monitorFPS() {
    let lastTime = performance.now();
    let frames = 0;
    
    const tick = (currentTime) => {
      frames++;
      
      if (currentTime - lastTime >= 1000) {
        const fps = Math.round((frames * 1000) / (currentTime - lastTime));
        
        // Adjust animations based on FPS
        if (fps < 30) {
          this.reducedMotion();
        }
        
        frames = 0;
        lastTime = currentTime;
      }
      
      requestAnimationFrame(tick);
    };
    
    requestAnimationFrame(tick);
  },

  optimizeAnimations() {
    // Disable animations if user prefers reduced motion
    if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
      this.reducedMotion();
    }
  },

  reducedMotion() {
    document.documentElement.style.setProperty('--animation-fast', '0.05s');
    document.documentElement.style.setProperty('--animation-normal', '0.1s');
    document.documentElement.style.setProperty('--animation-slow', '0.15s');
    
    // Disable complex animations
    utils.getElements('.shape').forEach(shape => {
      shape.style.animation = 'none';
    });
  }
};

// Accessibility enhancements
const accessibility = {
  init() {
    this.setupKeyboardNavigation();
    this.setupFocusManagement();
    this.setupARIA();
  },

  setupKeyboardNavigation() {
    // Tab navigation for custom elements
    const interactiveElements = utils.getElements(`
      .cta-button,
      .tab-button,
      .feature-card,
      .demo-card,
      .tool-card
    `);
    
    interactiveElements.forEach((element, index) => {
      if (!element.hasAttribute('tabindex')) {
        element.setAttribute('tabindex', '0');
      }
      
      element.addEventListener('keydown', (e) => {
        if (e.key === 'Enter' || e.key === ' ') {
          e.preventDefault();
          element.click();
        }
      });
    });
  },

  setupFocusManagement() {
    // Focus indicators
    const style = document.createElement('style');
    style.textContent = `
      :focus-visible {
        outline: 2px solid var(--primary);
        outline-offset: 2px;
        border-radius: 4px;
      }
      
      :focus:not(:focus-visible) {
        outline: none;
      }
    `;
    document.head.appendChild(style);
  },

  setupARIA() {
    // Add ARIA labels to interactive elements
    utils.getElements('.demo-video-container').forEach(container => {
      const title = container.closest('.demo-card')?.querySelector('.demo-title')?.textContent;
      if (title) {
        container.setAttribute('aria-label', `Play ${title} demo video`);
      }
    });
    
    // Update tab accessibility
    utils.getElements('.tab-button').forEach(button => {
      const tabId = button.dataset.tab;
      button.setAttribute('aria-controls', tabId);
      button.setAttribute('role', 'tab');
    });
    
    utils.getElements('.tab-content').forEach(content => {
      content.setAttribute('role', 'tabpanel');
    });
  }
};

// Error handling and logging
const errorHandler = {
  init() {
    window.addEventListener('error', this.handleError.bind(this));
    window.addEventListener('unhandledrejection', this.handlePromiseRejection.bind(this));
  },

  handleError(event) {
    console.warn('Script error:', event.error);
    // Graceful degradation - continue without throwing
  },

  handlePromiseRejection(event) {
    console.warn('Promise rejection:', event.reason);
    event.preventDefault(); // Prevent default handling
  }
};

// Main application initialization
class App {
  constructor() {
    this.modules = [
      themeManager,
      animationSystem,
      interactiveElements,
      performance,
      accessibility,
      errorHandler
    ];
  }

  async init() {
    try {
      // Wait for DOM to be ready
      if (document.readyState === 'loading') {
        await new Promise(resolve => {
          document.addEventListener('DOMContentLoaded', resolve);
        });
      }

      // Initialize all modules
      this.modules.forEach(module => {
        try {
          module.init();
        } catch (error) {
          console.warn(`Failed to initialize module:`, module, error);
        }
      });

      // Set loaded state
      state.isLoaded = true;
      document.body.classList.add('app-loaded');

      // Add loading completion animation
      this.showLoadingComplete();

    } catch (error) {
      console.error('Failed to initialize app:', error);
      // Fallback initialization
      this.fallbackInit();
    }
  }

  showLoadingComplete() {
    // Add a subtle completion indicator
    const indicator = document.createElement('div');
    indicator.style.cssText = `
      position: fixed;
      top: 0;
      left: 0;
      right: 0;
      height: 3px;
      background: var(--gradient-primary);
      z-index: 10000;
      transform: scaleX(0);
      transform-origin: left;
      animation: loadComplete 1s ease-out forwards;
    `;
    
    // Add completion animation
    if (!document.head.querySelector('#load-complete-styles')) {
      const style = document.createElement('style');
      style.id = 'load-complete-styles';
      style.textContent = `
        @keyframes loadComplete {
          0% { transform: scaleX(0); }
          70% { transform: scaleX(1); }
          100% { transform: scaleX(1); opacity: 0; }
        }
      `;
      document.head.appendChild(style);
    }
    
    document.body.appendChild(indicator);
    
    setTimeout(() => {
      indicator.remove();
    }, 1000);
  }

  fallbackInit() {
    // Basic functionality for when modules fail
    console.log('Running in fallback mode');
    
    // At minimum, set up theme
    try {
      themeManager.init();
    } catch (error) {
      console.warn('Theme manager failed:', error);
    }
    
    // Basic smooth scrolling
    document.querySelectorAll('a[href^="#"]').forEach(link => {
      link.addEventListener('click', (e) => {
        const target = document.querySelector(link.getAttribute('href'));
        if (target) {
          e.preventDefault();
          target.scrollIntoView({ behavior: 'smooth' });
        }
      });
    });
  }

  // Public API for external interaction
  getState() {
    return { ...state };
  }

  switchTab(tabId) {
    if (interactiveElements.switchTab) {
      interactiveElements.switchTab(tabId);
    }
  }

  animateElement(element, animation, delay) {
    if (animationSystem.animateIn) {
      return animationSystem.animateIn(element, animation, delay);
    }
  }
}

// Initialize the application
const app = new App();
app.init();

// Export for external use
window.RokaAnimationTools = {
  app,
  utils,
  state: () => app.getState()
};

// Development helpers
if (typeof process !== 'undefined' && process?.env?.NODE_ENV === 'development') {
  window.devTools = {
    animationSystem,
    interactiveElements,
    themeManager,
    utils
  };
}