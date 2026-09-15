import {computed, ref} from 'vue';
import {HostEvents} from '@qping/plugin-bus/web';
import {bus, text} from './i18n.js';

const revision = ref(0);
bus.on(HostEvents.Initialize, () => revision.value++);
bus.on(HostEvents.LanguageChanged, () => revision.value++);

export function useText() {
    return computed(() => {
        revision.value;
        return {...text};
    });
}
