// Paytable Canvas Renderer
const PaytableCanvas = (function() {
    let canvas, ctx;
    let currentBet = 0;
    let highlightHand = null;
    let highlightAmount = 0;

    const rowData = [
        { hand: 'RoyalFlush', name: 'ROYAL FLUSH', mult: 500, color: '#ff4444', isMajor: true },
        { hand: 'StraightFlush', name: 'STRAIGHT FLUSH', mult: 50, color: '#ff8800', hasJP: true },
        { hand: 'FourOfAKind', name: '4 OF A KIND', mult: 20, color: '#00cc00' },
        { hand: 'FullHouse', name: 'FULL HOUSE', mult: 7, color: '#00cccc' },
        { hand: 'Flush', name: 'FLUSH', mult: 5, color: '#0088ff' },
        { hand: 'Straight', name: 'STRAIGHT', mult: 4, color: '#00cccc' },
        { hand: 'ThreeOfAKind', name: '3 OF A KIND', mult: 3, color: '#00cc00' },
        { hand: 'TwoPair', name: '2 PAIR', mult: 2, color: '#00cccc' }
    ];

    function init() {
        canvas = document.getElementById('paytable-canvas');
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

    function setBet(bet) {
        currentBet = bet;
        render();
    }

    function setHighlight(hand, amount) {
        highlightHand = hand;
        highlightAmount = amount;
        render();
    }

    function formatNum(num) {
        if (!num || isNaN(num) || num <= 0) return "--";
        return num.toString().replace(/\B(?=(\d{3})+(?!\d))/g, ",");
    }

    function render() {
        if (!ctx) return;
        
        // Clear canvas
        ctx.clearRect(0, 0, canvas.width, canvas.height);
        
        const width = canvas.width / window.devicePixelRatio;
        const height = canvas.height / window.devicePixelRatio;
        
        const rowHeight = height / rowData.length;
        const fontSize = rowHeight * 0.7;
        
        ctx.font = `bold ${fontSize}px "Orbitron", sans-serif`;
        ctx.textBaseline = 'middle';
        
        rowData.forEach((row, i) => {
            const y = (i * rowHeight) + (rowHeight / 2);
            const isHighlighted = (row.hand === highlightHand);
            
            // Draw background if highlighted
            if (isHighlighted) {
                ctx.fillStyle = row.color;
                ctx.globalAlpha = 0.3;
                ctx.fillRect(0, i * rowHeight, width, rowHeight);
                ctx.globalAlpha = 1.0;
            }
            
            // Draw text
            ctx.fillStyle = isHighlighted ? '#ffffff' : row.color;
            ctx.textAlign = 'left';
            ctx.fillText(row.name, 10, y);
            
            // Draw multiplier/amount
            ctx.textAlign = 'right';
            const val = isHighlighted && highlightAmount > 0 ? highlightAmount : (row.mult * currentBet);
            ctx.fillText(formatNum(val), width - 10, y);
            
            // Draw JP tags if needed
            if (row.hasJP) {
                ctx.font = `bold ${fontSize * 0.5}px "Orbitron", sans-serif`;
                ctx.fillStyle = '#ffff00';
                ctx.fillText('+JP', width - 10 - ctx.measureText(formatNum(val)).width - 5, y - fontSize * 0.2);
                ctx.font = `bold ${fontSize}px "Orbitron", sans-serif`;
            }
        });
    }

    return {
        init,
        setBet,
        setHighlight,
        render
    };
})();
