// Double Up Board Canvas Renderer
const DuBoardCanvas = (function() {
    let canvas, ctx;
    let dealerCard = null;
    let challengerCards = [];
    let state = 'hidden'; // 'hidden', 'active', 'result'
    let currentBonus = 0;
    
    function init() {
        canvas = document.getElementById('du-board-canvas');
        if (!canvas) return;
        ctx = canvas.getContext('2d');
        resize();
        window.addEventListener('resize', resize);
        render();
    }

    function resize() {
        if (!canvas) return;
        const rect = canvas.parentElement.getBoundingClientRect();
        canvas.width = rect.width * window.devicePixelRatio;
        canvas.height = rect.height * window.devicePixelRatio;
        ctx.scale(window.devicePixelRatio, window.devicePixelRatio);
        render();
    }

    function setState(newState, bonus) {
        state = newState;
        currentBonus = bonus || 0;
        if (state === 'hidden' && canvas) {
            canvas.style.display = 'none';
        } else if (canvas) {
            canvas.style.display = 'block';
        }
        render();
    }

    function setCards(dealer, challengerTrail) {
        dealerCard = dealer;
        challengerCards = challengerTrail || [];
        render();
    }
    
    function formatNum(num) {
        if (!num || isNaN(num) || num <= 0) return "0";
        return num.toString().replace(/\B(?=(\d{3})+(?!\d))/g, ",");
    }

    function render() {
        if (!ctx || state === 'hidden') return;
        
        ctx.clearRect(0, 0, canvas.width, canvas.height);
        
        const width = canvas.width / window.devicePixelRatio;
        const height = canvas.height / window.devicePixelRatio;
        
        // Background
        ctx.fillStyle = '#001133';
        ctx.fillRect(0, 0, width, height);
        ctx.strokeStyle = '#00ffff';
        ctx.lineWidth = 2;
        ctx.strokeRect(0, 0, width, height);
        
        // Title text
        ctx.font = `bold ${height * 0.25}px "Orbitron", sans-serif`;
        ctx.fillStyle = '#ffff00';
        ctx.textAlign = 'center';
        ctx.textBaseline = 'top';
        ctx.fillText("DOUBLE UP", width / 2, 5);
        
        // Bonus text
        if (currentBonus > 0) {
            ctx.font = `bold ${height * 0.2}px "Orbitron", sans-serif`;
            ctx.fillStyle = '#00ff00';
            ctx.fillText(`BONUS: ${formatNum(currentBonus)}`, width / 2, height * 0.35);
        }
        
        // Cards area text
        ctx.font = `bold ${height * 0.15}px "Orbitron", sans-serif`;
        ctx.fillStyle = '#ffffff';
        ctx.textAlign = 'left';
        ctx.fillText("DEALER", 10, height * 0.6);
        ctx.textAlign = 'right';
        ctx.fillText("PLAYER", width - 10, height * 0.6);
        
        // Draw card representations (placeholders for the canvas version since cards are usually DOM images)
        if (dealerCard) {
            drawCard(ctx, 10, height * 0.75, width * 0.1, height * 0.2, dealerCard);
        }
        
        challengerCards.forEach((card, i) => {
            const x = width - 10 - ((challengerCards.length - i) * (width * 0.12));
            drawCard(ctx, x, height * 0.75, width * 0.1, height * 0.2, card.card);
        });
    }
    
    function drawCard(ctx, x, y, w, h, cardCode) {
        ctx.fillStyle = '#ffffff';
        ctx.fillRect(x, y, w, h);
        ctx.strokeStyle = '#000000';
        ctx.strokeRect(x, y, w, h);
        
        if (cardCode) {
            ctx.fillStyle = (cardCode.includes('H') || cardCode.includes('D')) ? '#ff0000' : '#000000';
            ctx.font = `bold ${h * 0.8}px sans-serif`;
            ctx.textAlign = 'center';
            ctx.textBaseline = 'middle';
            const val = cardCode.substring(0, cardCode.length - 1).replace('T', '10');
            ctx.fillText(val, x + w/2, y + h/2);
        }
    }

    return {
        init,
        setState,
        setCards,
        render
    };
})();
