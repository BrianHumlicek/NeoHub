// Exit delay beep audio functionality
window.exitDelayBeep = {
    audioContext: null,
    beepInterval: null,
    isUrgent: false,

    // Start beeping (500ms interval when urgent, 1000ms when normal)
    startBeeping: function(urgent) {
        this.isUrgent = !!urgent;
        var interval = this.isUrgent ? 500 : 1000;

        // If already beeping, restart with new interval
        if (this.beepInterval) {
            clearInterval(this.beepInterval);
            this.beepInterval = null;
        }

        // Create audio context on first use
        if (!this.audioContext) {
            this.audioContext = new (window.AudioContext || window.webkitAudioContext)();
        }

        // Resume if suspended (mobile browsers require user interaction)
        if (this.audioContext.state === 'suspended') {
            this.audioContext.resume();
        }

        // Play immediately
        this.playBeep();

        // Then at the appropriate interval
        this.beepInterval = setInterval(() => {
            this.playBeep();
        }, interval);
    },

    // Stop beeping
    stopBeeping: function() {
        if (this.beepInterval) {
            clearInterval(this.beepInterval);
            this.beepInterval = null;
        }
        this.isUrgent = false;
    },

    // Play a single beep (higher pitch when urgent)
    playBeep: function() {
        if (!this.audioContext) {
            this.audioContext = new (window.AudioContext || window.webkitAudioContext)();
        }

        const oscillator = this.audioContext.createOscillator();
        const gainNode = this.audioContext.createGain();

        oscillator.connect(gainNode);
        gainNode.connect(this.audioContext.destination);

        oscillator.frequency.value = this.isUrgent ? 1200 : 800;
        oscillator.type = 'sine';

        gainNode.gain.setValueAtTime(0.3, this.audioContext.currentTime);
        gainNode.gain.exponentialRampToValueAtTime(0.01, this.audioContext.currentTime + 0.1);

        oscillator.start(this.audioContext.currentTime);
        oscillator.stop(this.audioContext.currentTime + 0.1);
    }
};
