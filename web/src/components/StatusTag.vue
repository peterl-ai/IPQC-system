<template><a-tag :color="color">{{ label }}</a-tag></template>

<script setup lang="ts">
import { computed } from 'vue'
const props = defineProps<{ tone: 'success' | 'warning' | 'danger' | 'info' | 'neutral'; label: string }>()
const color = computed(() => ({ success: 'green', warning: 'orange', danger: 'red', info: 'blue', neutral: 'default' })[props.tone])
</script>
