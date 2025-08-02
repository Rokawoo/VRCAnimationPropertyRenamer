import { baseLayerLuminance, StandardLuminance } from 'https://unpkg.com/@fluentui/web-components';

const setTheme = () => {
  const isDark = window.matchMedia("(prefers-color-scheme: dark)").matches;
  baseLayerLuminance.setValueFor(
    document.documentElement, 
    isDark ? StandardLuminance.DarkMode : StandardLuminance.LightMode
  );
};

const initVideoPlayers = () => {
  const videoContainers = document.querySelectorAll('.video-container');
  
  videoContainers.forEach(container => {
    const video = container.querySelector('video');
    const overlay = container.querySelector('.play-overlay');
    
    const togglePlay = () => {
      if (video.paused) {
        video.play();
        container.classList.add('playing');
      } else {
        video.pause();
        container.classList.remove('playing');
      }
    };
    
    overlay.addEventListener('click', togglePlay);
    video.addEventListener('click', togglePlay);
    
    video.addEventListener('play', () => {
      container.classList.add('playing');
      video.controls = true;
    });
    
    video.addEventListener('pause', () => {
      container.classList.remove('playing');
    });
    
    video.addEventListener('ended', () => {
      container.classList.remove('playing');
      video.controls = false;
    });
    
    container.addEventListener('mouseleave', () => {
      if (video.paused) {
        video.controls = false;
      }
    });
  });
};

const addScrollEffects = () => {
  const cards = document.querySelectorAll('.demo-card');
  
  const observer = new IntersectionObserver((entries) => {
    entries.forEach((entry, index) => {
      if (entry.isIntersecting) {
        setTimeout(() => {
          entry.target.style.opacity = '1';
          entry.target.style.transform = 'translateY(0)';
        }, index * 100);
      }
    });
  }, { threshold: 0.1 });
  
  cards.forEach(card => {
    card.style.opacity = '0';
    card.style.transform = 'translateY(30px)';
    card.style.transition = 'opacity 0.6s ease, transform 0.6s ease';
    observer.observe(card);
  });
};

const initParticleEffects = () => {
  const ctaButton = document.querySelector('.cta-button');
  
  ctaButton.addEventListener('mouseenter', () => {
    for (let i = 0; i < 6; i++) {
      setTimeout(() => createParticle(ctaButton), i * 50);
    }
  });
};

const createParticle = (element) => {
  const rect = element.getBoundingClientRect();
  const particle = document.createElement('div');
  
  particle.style.cssText = `
    position: fixed;
    width: 4px;
    height: 4px;
    background: rgba(255, 255, 255, 0.8);
    border-radius: 50%;
    pointer-events: none;
    z-index: 1000;
    left: ${rect.left + rect.width / 2}px;
    top: ${rect.top + rect.height / 2}px;
  `;
  
  document.body.appendChild(particle);
  
  const angle = Math.random() * Math.PI * 2;
  const velocity = 50 + Math.random() * 50;
  const life = 800 + Math.random() * 400;
  
  particle.animate([
    { transform: 'translate(0, 0) scale(1)', opacity: 1 },
    { 
      transform: `translate(${Math.cos(angle) * velocity}px, ${Math.sin(angle) * velocity}px) scale(0)`, 
      opacity: 0 
    }
  ], {
    duration: life,
    easing: 'cubic-bezier(0.25, 0.46, 0.45, 0.94)'
  }).onfinish = () => particle.remove();
};

document.addEventListener('DOMContentLoaded', () => {
  setTheme();
  initVideoPlayers();
  addScrollEffects();
  initParticleEffects();
});

window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', setTheme);