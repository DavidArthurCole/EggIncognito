(() => {
    const OPEN = 'tt-open';
    const EDGE = 8;
    const GAP = 6;

    const popFor = trigger => trigger.querySelector(':scope > .tt-pop');

    const triggerFor = target => (target instanceof Element ? target.closest('.tt') : null);

    function place(trigger, pop) {
        pop.classList.add(OPEN);
        const anchor = trigger.getBoundingClientRect();
        const box = pop.getBoundingClientRect();
        const maxLeft = window.innerWidth - box.width - EDGE;
        const left = Math.max(EDGE, Math.min(anchor.left + (anchor.width - box.width) / 2, maxLeft));
        const above = anchor.top - box.height - GAP;
        const top = above < EDGE ? anchor.bottom + GAP : above;
        pop.style.left = `${Math.round(left)}px`;
        pop.style.top = `${Math.round(top)}px`;
    }

    function show(trigger) {
        const pop = popFor(trigger);
        if (pop) place(trigger, pop);
    }

    function hide(trigger) {
        const pop = popFor(trigger);
        if (pop) pop.classList.remove(OPEN);
    }

    function hideAll() {
        for (const pop of document.querySelectorAll(`.tt-pop.${OPEN}`)) pop.classList.remove(OPEN);
    }

    const onEnter = e => {
        const trigger = triggerFor(e.target);
        if (trigger) show(trigger);
    };

    const onLeave = e => {
        const trigger = triggerFor(e.target);
        if (trigger && !trigger.contains(e.relatedTarget)) hide(trigger);
    };

    document.addEventListener('pointerover', onEnter);
    document.addEventListener('pointerout', onLeave);
    document.addEventListener('focusin', onEnter);
    document.addEventListener('focusout', onLeave);
    document.addEventListener('keydown', e => {
        if (e.key === 'Escape') hideAll();
    });
    window.addEventListener('scroll', hideAll, true);
    window.addEventListener('resize', hideAll);
})();
